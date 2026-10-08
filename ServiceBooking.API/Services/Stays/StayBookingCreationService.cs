using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Subjects;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record StayStayInput(DateOnly CheckIn, DateOnly CheckOut, int Adults, int Children, int Dogs, bool NeedCot);

public sealed record StayProblem(StayRefusalCode Code, string Message);

/// <summary>The verdict of the rules on one stay: the problems in the order of checking (dates → guests → prices) and the money when it can be computed.</summary>
public sealed record StayEvaluation(
    IReadOnlyList<StayProblem> Problems, StayQuoteResult? Money, IReadOnlyList<NightPrice> Nights, DateOnly Today, StayRulesSettings Rules);

public sealed record StayHouseContext(House House, Company Company, StaysSettings Settings);

public sealed record StayCreateResult(ActionResult? Error, StayBooking? Booking = null, bool Created = false);

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.7.1, API_CONTRACT_CYCLE37.md §37.23–§37.25 — the quote and the creation of a booking. The order of checks of
/// <see cref="CreateAsync"/> is the contract's and each step has a reason: cheap form checks; the house; IDEMPOTENCY (a repeat of an already created booking
/// answers 200 before the one-shot captcha and the limits); the guest; the company gate; the number's limits under the number's lock; then, under the HOUSE
/// lock: lazy release of expired holds → occupancy → the rules → the money → snapshots → the occupancy row + the journal. A booking is never created with other
/// data than the guest confirmed: every refusal is a JSON 409 and nothing is written.
/// </summary>
public class StayBookingCreationService(
    AppDbContext db, CaptchaService captcha, SubjectScopeResolver subjectScope, HouseOccupancyWriter occupancy, StayPhoneThrottle throttle,
    StayBookingEventLog eventLog, StayHoldExpirer expirer, LegalDocumentProvider legalProvider, IOptions<StaysOptions> options, IStaysClock clock,
    StaysCompanyService companyService, ShopChannelReader channelReader, StayActorResolver actors, CheckInInfoReleaser checkInInfo)
{
    private const string IdempotencyIndex = "IX_StayBookings_CompanyId_IdempotencyKey";

    // ── lookups ──

    /// <summary>A bookable house by id: published, not archived, in a «Дома» company. Null otherwise (404, indistinguishable). A blocked company is still found.</summary>
    public async Task<StayHouseContext?> FindPublicHouseAsync(Guid houseId, CancellationToken ct)
    {
        var house = await db.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.Id == houseId && h.IsPublished && h.ArchivedAtUtc == null, ct);
        if (house is null) return null;
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == house.CompanyId && c.Kind == CompanyKind.Stays, ct);
        if (company is null) return null;
        return new StayHouseContext(house, company, await companyService.LoadSettingsAsync(company.Id, ct: ct));
    }

    public async Task<StayHouseContext?> FindHouseOfCompanyAsync(Guid companyId, Guid houseId, CancellationToken ct)
    {
        var house = await db.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.Id == houseId && h.CompanyId == companyId && h.ArchivedAtUtc == null, ct);
        var company = house is null ? null : await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId && c.Kind == CompanyKind.Stays, ct);
        return house is null || company is null ? null : new StayHouseContext(house, company, await companyService.LoadSettingsAsync(companyId, ct: ct));
    }

    // ── the rules on one stay (shared by quote, create, manual) ──

    public async Task<StayEvaluation> EvaluateAsync(StayHouseContext ctx, StayStayInput input, bool manual, DateTime nowUtc, CancellationToken ct)
    {
        var (house, company, settings) = ctx;
        var today = StayTime.LocalDate(company.TimeZoneId, nowUtc);
        var nights = input.CheckOut.DayNumber - input.CheckIn.DayNumber;
        var occupancies = nights is > 0 and <= StayEvaluator.MaxNightsComputed
            ? await occupancy.LoadActiveAsync(house.Id, input.CheckIn, input.CheckOut, ct) : [];
        IReadOnlyList<PricePeriodValue> periods = house.PriceMode == HousePriceMode.ByDates && nights is > 0 and <= StayEvaluator.MaxNightsComputed
            ? HouseService.ToValues(await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == house.Id && p.EndDate >= input.CheckIn && p.StartDate < input.CheckOut).ToListAsync(ct))
            : [];
        return StayEvaluator.Evaluate(HouseFacts.Of(house), SettingsFacts.Of(settings), periods, occupancies, input, today, nowUtc, manual);
    }

    // ── quote ──

    public async Task<StayQuoteDto> QuoteAsync(StayHouseContext ctx, StayStayInput input, bool checkGate, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var eval = await EvaluateAsync(ctx, input, manual: !checkGate, now, ct);
        var gate = checkGate ? await companyService.EvaluateGateAsync(ctx.Company, ctx.Settings, ct) : GateResult.Ok;
        return BuildQuoteDto(ctx, eval, gate);
    }

    public StayQuoteDto BuildQuoteDto(StayHouseContext ctx, StayEvaluation eval, GateResult gate)
    {
        var s = ctx.Settings;
        var m = eval.Money;
        // Problems that prevent a computation leave the money at zero (API_CONTRACT_CYCLE37.md §37.23).
        var blocking = eval.Problems.Any(p => p.Code is StayRefusalCode.InvalidDates or StayRefusalCode.TooManyGuests or StayRefusalCode.DogsNotAllowed
            or StayRefusalCode.CotNotAvailable or StayRefusalCode.NoPriceForNights);
        if (blocking) m = null;
        return new StayQuoteDto(
            eval.Problems.Count == 0, eval.Problems.Select(p => new ProblemDto(p.Code, p.Message)).ToList(), m is null ? 0 : eval.Nights.Count,
            m is null ? [] : eval.Nights.Select(n => new NightPriceDto(n.Date, n.PriceRub)).ToList(),
            m?.Lines.Select(l => new StayChargeLineDto(l.Kind, l.Label, l.Quantity, l.UnitPriceRub, l.Nights, l.AmountRub, l.PrepayEligible)).ToList() ?? [],
            m?.ExtraBeds ?? 0, m?.TotalRub ?? 0, s.PrepayPercent, m?.PrepayRub ?? 0, m?.DueAtCheckInRub ?? 0, m?.AverageNightRub ?? 0, s.HoldMinutes,
            StayFormat.Time(s.CheckInTime), StayFormat.Time(s.CheckOutTime), s.CancellationPolicy, StaysTexts.CancellationSummary(s.CancellationPolicy),
            gate.Accepting, gate.Accepting ? null : StaysTexts.NotAcceptingGuest);
    }

    // ── create (a guest) ──

    public async Task<StayCreateResult> CreateAsync(Guid houseId, CreateStayBookingInput dto, ClaimsPrincipal user, string? remoteIp, CancellationToken ct)
    {
        // 1. The form. Text fields are nullable in the DTO on purpose: the contract's own Russian sentences answer these.
        if (dto.CheckIn is null || dto.CheckOut is null) return Bad("Укажите даты заезда и выезда");
        if (dto.Adults is < 1 or > 30) return Bad("Взрослых — от 1 до 30");
        if (dto.Children is < 0 or > 30) return Bad("Детей — от 0 до 30");
        if (dto.Dogs is < 0 or > 20) return Bad("Собак — от 0 до 20");
        var guestName = (dto.GuestName ?? string.Empty).Trim();
        if (guestName.Length == 0) return Bad("Укажите имя");
        if (guestName.Length > 100) return Bad("Имя — не длиннее 100 символов");
        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        if (comment is { Length: > 500 }) return Bad("Комментарий — не длиннее 500 символов");
        if (dto.IdempotencyKey is null || dto.IdempotencyKey == Guid.Empty) return Bad("Нужен ключ запроса — обновите страницу");
        var idempotencyKey = dto.IdempotencyKey.Value;

        // 2. The house: none / not published / archived / not a «Дома» company → 404; a blocked company → 409.
        var ctx = await FindPublicHouseAsync(houseId, ct);
        if (ctx is null) return new StayCreateResult(new NotFoundResult());
        var (house, company, settings) = ctx;
        if (!TryArrival(dto.ArrivalTime, settings.CheckInTime, out var arrival))
            return Bad("Время прибытия — от времени заезда до 23:30 с шагом 30 минут");
        if (!company.IsActive) return Refuse(StayRefusalCode.NotAcceptingBookings, StaysTexts.NotAcceptingGuest, NotAcceptingReason.CompanyBlocked);

        // 3. Idempotency — BEFORE the captcha and the limits.
        var existing = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null) return new StayCreateResult(null, existing, Created: false);

        // 4. The guest.
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = userId is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        string canonicalPhone;
        StayActorKind guestKind;
        if (account is not null)
        {
            // A signed-in guest books on THEIR number — a phone typed into the form is ignored (deviation 8, §37.18).
            if (account.PhoneNumber is not null) canonicalPhone = account.PhoneNumber;
            else if (!TryPhone(dto.GuestPhone, out canonicalPhone)) return Bad(PhoneText);
            guestKind = StayActorKind.Customer;
        }
        else
        {
            if (captcha.IsEnforced)
            {
                if (string.IsNullOrEmpty(dto.CaptchaToken)) return Bad("Подтвердите, что вы не робот");
                if (!await captcha.ValidateAsync(dto.CaptchaToken, remoteIp)) return Bad("Подтвердите, что вы не робот");
            }
            if (!TryPhone(dto.GuestPhone, out canonicalPhone)) return Bad(PhoneText);
            guestKind = StayActorKind.Guest;
        }

        // 5. The company gate.
        var gate = await companyService.EvaluateGateAsync(company, settings, ct);
        if (!gate.Accepting) return Refuse(StayRefusalCode.NotAcceptingBookings, StaysTexts.NotAcceptingGuest, gate.ReasonCode);

        var now = clock.UtcNow;
        var input = new StayStayInput(dto.CheckIn.Value, dto.CheckOut.Value, dto.Adults, dto.Children, dto.Dogs, dto.NeedCot);
        var notifyByMessenger = dto.NotifyByMessenger && settings.GuestMessengerEnabled && await channelReader.IsMessengerAvailableAsync(company.Id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // 6. The number's limits, under the number's lock (lock order: phone → house, §37.5.1).
        await AdvisoryLock.AcquireAsync(db, $"stay-guest-phone:{canonicalPhone}");
        switch (await throttle.CheckAsync(company.Id, canonicalPhone, now, ct))
        {
            case StayThrottleVerdict.TooManyHeld: return Throttled(StayPhoneThrottle.HeldText);
            case StayThrottleVerdict.TooManyPerDay: return Throttled(StayPhoneThrottle.PerDayText);
        }

        // 7. The house lock: lazy release → occupancy → rules → money.
        await occupancy.LockHouseAsync(house.Id);
        await expirer.ExpireOverlappingAsync(house.Id, input.CheckIn, input.CheckOut, now, ct);
        var eval = await EvaluateAsync(ctx, input, manual: false, now, ct);
        if (eval.Problems.Count > 0) return Refuse(eval.Problems[0].Code, eval.Problems[0].Message);
        var money = eval.Money!;
        if (money.TotalRub != dto.ExpectedTotalRub)
        {
            var quote = BuildQuoteDto(ctx, eval, gate);
            return new StayCreateResult(new ConflictObjectResult(new StayRefusalDto(StayRefusalCode.PriceChanged, StaysTexts.PriceChanged(money.TotalRub), null, quote)));
        }

        // 8. The booking with every snapshot.
        var held = money.PrepayRub > 0;
        var booking = NewBooking(ctx, input, eval, money, now);
        booking.IdempotencyKey = idempotencyKey;
        booking.Status = held ? StayBookingStatus.Held : StayBookingStatus.Confirmed;
        booking.HoldExpiresAtUtc = held ? now.AddMinutes(settings.HoldMinutes) : null;
        booking.GuestKind = guestKind;
        booking.GuestUserId = guestKind == StayActorKind.Customer ? account!.Id : null;
        booking.GuestName = guestName;
        booking.GuestPhone = canonicalPhone;
        booking.Comment = comment;
        booking.ArrivalTime = arrival;
        booking.NotifyByMessenger = notifyByMessenger;
        booking.MessengerConsentVersion = notifyByMessenger ? legalProvider.Current?.GetText(LegalTextKey.StayMessengerConsent)?.Version : null;
        booking.MessengerConsentAtUtc = notifyByMessenger ? now : null;
        booking.PaymentDetailsSnapshot = settings.PaymentDetails;
        booking.PaymentPurposeSnapshot = settings.PaymentPurpose;
        ApplyVersions(booking, guestKind, now);

        db.StayBookings.Add(booking);
        db.StayBookingCharges.AddRange(ChargesOf(booking.Id, money));
        occupancy.AddBooking(booking, now);
        var actor = await actors.ResolveGuestAsync(user, guestName, ct);
        await eventLog.AppendAsync(booking, StayBookingEventKind.Created, actor, null, booking.Status);
        if (!held) await checkInInfo.ReleaseIfDueAsync(booking, StayActor.System, ct);

        // 9. Save. A race with another booking / block is the database's EXCLUDE constraint; a race of the same key is the unique index.
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (HouseOccupancyWriter.IsOverlapViolation(ex))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Refuse(StayRefusalCode.DatesUnavailable, StaysTexts.DatesUnavailable);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == IdempotencyIndex)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.IdempotencyKey == idempotencyKey, ct);
            if (winner is null) throw;
            return new StayCreateResult(null, winner, Created: false);
        }
        return new StayCreateResult(null, booking, Created: true);
    }

    // ── manual booking (staff, P1) ──

    public async Task<StayCreateResult> CreateManualAsync(Guid companyId, ManualStayBookingInput dto, StayActor actor, CancellationToken ct)
    {
        if (dto.HouseId is null) return Bad("Выберите дом");
        if (dto.CheckIn is null || dto.CheckOut is null) return Bad("Укажите даты заезда и выезда");
        if (dto.Adults is < 1 or > 30) return Bad("Взрослых — от 1 до 30");
        if (dto.Children is < 0 or > 30) return Bad("Детей — от 0 до 30");
        if (dto.Dogs is < 0 or > 20) return Bad("Собак — от 0 до 20");
        var guestName = (dto.GuestName ?? string.Empty).Trim();
        if (guestName.Length == 0) return Bad("Укажите имя");
        if (guestName.Length > 100) return Bad("Имя — не длиннее 100 символов");
        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        if (comment is { Length: > 500 }) return Bad("Комментарий — не длиннее 500 символов");
        if (dto.TotalOverrideRub is < 0 or > 10_000_000) return Bad("Итог — от 0 до 10 000 000 ₽");
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(dto.GuestPhone))
        {
            if (!TryPhone(dto.GuestPhone, out var canonical)) return Bad(PhoneText);
            phone = canonical;
        }
        if (dto.NotifyGuest && phone is null) return Bad("Чтобы уведомить гостя, укажите телефон");

        var ctx = await FindHouseOfCompanyAsync(companyId, dto.HouseId.Value, ct);
        if (ctx is null) return new StayCreateResult(new NotFoundResult());
        var (house, company, settings) = ctx;
        var input = new StayStayInput(dto.CheckIn.Value, dto.CheckOut.Value, dto.Adults, dto.Children, dto.Dogs, dto.NeedCot);
        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await occupancy.LockHouseAsync(house.Id);
        await expirer.ExpireOverlappingAsync(house.Id, input.CheckIn, input.CheckOut, now, ct);
        var eval = await EvaluateAsync(ctx, input, manual: true, now, ct);
        if (eval.Problems.Count > 0) return Refuse(eval.Problems[0].Code, eval.Problems[0].Message);
        var money = eval.Money!;

        var booking = NewBooking(ctx, input, eval, money, now);
        booking.IdempotencyKey = Guid.NewGuid();
        booking.IsManual = true;
        booking.Status = StayBookingStatus.Confirmed;
        booking.GuestKind = StayActorKind.Staff;
        booking.GuestName = guestName;
        booking.GuestPhone = phone;
        booking.Comment = comment;
        booking.NotifyByMessenger = dto.NotifyGuest && settings.GuestMessengerEnabled;
        booking.PrepayPercentSnapshot = 0;
        booking.PrepayRub = 0;
        var charges = ChargesOf(booking.Id, money);
        if (dto.TotalOverrideRub is { } total)
        {
            booking.TotalRub = total;
            charges = [new StayBookingCharge
            {
                Id = Guid.NewGuid(), StayBookingId = booking.Id, Position = 0, Kind = StayChargeKind.ManualTotal, Label = "Итог изменён вручную", Quantity = 1,
                UnitPriceRub = total, NightsCount = booking.Nights, AmountRub = total, PrepayEligible = false,
            }];
        }
        booking.DueAtCheckInRub = booking.TotalRub;
        db.StayBookings.Add(booking);
        db.StayBookingCharges.AddRange(charges);
        occupancy.AddBooking(booking, now);
        await eventLog.AppendAsync(booking, StayBookingEventKind.Created, actor, null, booking.Status);
        await checkInInfo.ReleaseIfDueAsync(booking, StayActor.System, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (HouseOccupancyWriter.IsOverlapViolation(ex))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Refuse(StayRefusalCode.DatesUnavailable, StaysTexts.DatesUnavailable);
        }
        return new StayCreateResult(null, booking, Created: true);
    }

    // ── helpers ──

    private StayBooking NewBooking(StayHouseContext ctx, StayStayInput input, StayEvaluation eval, StayQuoteResult money, DateTime now)
    {
        var (house, company, settings) = ctx;
        return new StayBooking
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, HouseId = house.Id, PublicToken = PublicStayToken.Generate(), Version = 1,
            CheckInDate = input.CheckIn, CheckOutDate = input.CheckOut, Nights = input.CheckOut.DayNumber - input.CheckIn.DayNumber,
            Adults = input.Adults, Children = input.Children, Dogs = input.Dogs, NeedCot = input.NeedCot, ExtraBeds = money.ExtraBeds,
            TotalRub = money.TotalRub, PrepayRub = money.PrepayRub, DueAtCheckInRub = money.DueAtCheckInRub, PrepayPercentSnapshot = settings.PrepayPercent,
            NightPricesJson = StayDtoMapper.NightPricesJson(eval.Nights), CancellationPolicySnapshot = settings.CancellationPolicy,
            CheckInTimeSnapshot = settings.CheckInTime, CheckOutTimeSnapshot = settings.CheckOutTime, TimeZoneIdSnapshot = company.TimeZoneId,
            ProviderSnapshotJson = StaysCompanyService.ProviderSnapshotJson(settings), CreatedAtUtc = now, UpdatedAtUtc = now,
        };
    }

    private void ApplyVersions(StayBooking booking, StayActorKind guestKind, DateTime now)
    {
        var snapshot = legalProvider.Current;
        booking.BookingNoticeVersion = snapshot?.GetText(LegalTextKey.StayBookingNotice)?.Version;
        booking.BookingTermsVersion = snapshot?.GetText(LegalTextKey.StayBookingTerms)?.Version;
        booking.CancellationTermsVersion = snapshot?.GetText(LegalTextKey.StayCancellationTerms)?.Version;
        // A guest's consent snapshot exactly like a guest booking / order: the versions in force right now, from the server.
        if (guestKind != StayActorKind.Guest) return;
        var privacy = snapshot?.Get(LegalDocumentType.Privacy);
        var terms = snapshot?.Get(LegalDocumentType.TermsClient);
        if (privacy is null || terms is null) return;
        booking.ConsentPrivacyVersion = privacy.Version;
        booking.ConsentTermsVersion = terms.Version;
        booking.ConsentAcceptedAtUtc = now;
    }

    private static List<StayBookingCharge> ChargesOf(Guid bookingId, StayQuoteResult money) =>
        money.Lines.Select((l, i) => new StayBookingCharge
        {
            Id = Guid.NewGuid(), StayBookingId = bookingId, Position = i, Kind = l.Kind, Label = l.Label, Quantity = l.Quantity, UnitPriceRub = l.UnitPriceRub,
            NightsCount = l.Nights, AmountRub = l.AmountRub, PrepayEligible = l.PrepayEligible,
        }).ToList();

    public const string PhoneText = "Введите номер телефона в формате +7 (900) 000-00-00";

    private static bool TryPhone(string? raw, out string canonical)
    {
        canonical = string.Empty;
        return !string.IsNullOrWhiteSpace(raw) && PhoneNormalizer.TryNormalizeRussian(raw, out canonical);
    }

    /// <summary>"HH:mm" on a 30-minute grid from the check-in time to 23:30, or null ("не знаю").</summary>
    private static bool TryArrival(string? raw, TimeOnly checkIn, out TimeOnly? arrival)
    {
        arrival = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!StayFormat.TryParseTime(raw, out var t) || !StayFormat.IsHalfHour(t) || t < checkIn || t > new TimeOnly(23, 30)) return false;
        arrival = t;
        return true;
    }

    private static StayCreateResult Bad(string message) => new(new BadRequestObjectResult(message));

    private static StayCreateResult Throttled(string message) => new(new ObjectResult(message) { StatusCode = StatusCodes.Status429TooManyRequests });

    private static StayCreateResult Refuse(StayRefusalCode code, string message, NotAcceptingReason? reason = null) =>
        new(new ConflictObjectResult(new StayRefusalDto(code, message, reason?.ToString())));
}
