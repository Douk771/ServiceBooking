using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Subjects;
using ServiceBooking.Core.Entities;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record ServiceOrderCreateResult(ActionResult? Error, StayServiceOrder? Order = null, bool Created = false);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.7.1, API_CONTRACT_CYCLE39.md §39.22.5 — the order of a service WITHOUT a stay. The order of the checks is the contract's and each step has a reason:
/// cheap form checks; the service; the switch of the company; IDEMPOTENCY (a repeat answers 200 before the one-shot captcha and the limits); the guest; the gate (the executor
/// is mandatory ALWAYS, ЮР39-2); the number's limits under the number's lock; then, under the lock of the SERVICE: lazy release of expired holds → the rules → the money →
/// snapshots → the order, its one session and the journal. Nothing is created with other data than the guest confirmed: every refusal is a JSON 409 and nothing is written.
/// </summary>
public class ServiceOrderCreationService(
    AppDbContext db, CaptchaService captcha, ServiceSlotService slots, ServiceSessionWriter sessionWriter, ServiceOrderThrottle throttle,
    StayServiceOrderEventLog eventLog, ServiceHoldReleaser holdReleaser, LegalDocumentProvider legalProvider, IStaysClock clock,
    StaysCompanyService companyService, ShopChannelReader channelReader, StayActorResolver actors)
{
    private const string IdempotencyIndex = "IX_StayServiceOrders_CompanyId_IdempotencyKey";

    /// <summary>The form checks shared by the guest's order, the quote and the staff's forms. Returns the Russian 400 sentence or null.</summary>
    public static string? ValidateSelection(DateOnly? date, int? startMinute, int? hours, IReadOnlyList<ItemSelectionInput>? items, out ServiceSelection selection)
    {
        selection = new ServiceSelection(default, 0, 0, []);
        if (date is null) return "Выберите дату";
        if (startMinute is null) return "Выберите время начала";
        if (startMinute % ServiceScheduleRules.GridMinutes != 0 || startMinute is < 0 or > 2880) return "Время — с шагом 30 минут";
        if (hours is null or < 1 or > 12) return "Укажите число часов";
        var list = items ?? [];
        if (list.Count > 20) return "Количество — от 0 до 50";
        foreach (var item in list)
            if (item.ItemId is null || item.Quantity is < 0 or > 50) return "Количество — от 0 до 50";
        selection = new ServiceSelection(date.Value, startMinute.Value, hours.Value, list);
        return null;
    }

    public async Task<ServiceOrderCreateResult> CreateAsync(CompanyKind kind, Guid serviceId, CreateServiceOrderInput dto, ClaimsPrincipal user, string? remoteIp, CancellationToken ct)
    {
        // 1. The form (text fields are nullable in the DTO on purpose: the contract's own Russian sentences answer these).
        var formError = ValidateSelection(dto.BusinessDate, dto.StartMinute, dto.Hours, dto.Items, out var selection);
        if (formError is not null) return Bad(formError);
        var guestName = (dto.GuestName ?? string.Empty).Trim();
        if (guestName.Length == 0) return Bad("Укажите имя");
        if (guestName.Length > 100) return Bad("Имя — не длиннее 100 символов");
        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        if (comment is { Length: > 500 }) return Bad("Комментарий — не длиннее 500 символов");
        if (dto.IdempotencyKey is null || dto.IdempotencyKey == Guid.Empty) return Bad("Нужен ключ запроса — обновите страницу");
        var idempotencyKey = dto.IdempotencyKey.Value;

        // 2. The service: none / not published / archived / not a «Дома» company → 404 (indistinguishable); a blocked company → 409.
        var scope = await slots.FindPublicAsync(kind, serviceId, ct);
        if (scope is null) return new ServiceOrderCreateResult(new NotFoundResult());
        var (service, company, settings) = scope;
        if (!company.IsActive) return Refuse(ServiceRefusalCode.NotAcceptingBookings, ServiceTexts.NotAcceptingGuest, NotAcceptingReason.CompanyBlocked);

        // 3. A stand-alone order is switched off by default.
        if (!settings.AcceptServiceOrdersWithoutStay) return Refuse(ServiceRefusalCode.ServiceOrdersDisabled, ServiceTexts.OrdersDisabled);

        // 4. Idempotency — BEFORE the captcha and the limits.
        var existing = await db.StayServiceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == company.Id && o.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null) return new ServiceOrderCreateResult(null, existing, Created: false);

        // 5. The guest.
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = userId is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        string canonicalPhone;
        StayActorKind guestKind;
        if (account is not null)
        {
            // A signed-in guest orders on THEIR number — a phone typed into the form is ignored.
            if (account.PhoneNumber is not null) canonicalPhone = account.PhoneNumber;
            else if (!TryPhone(dto.GuestPhone, out canonicalPhone)) return Bad(StayBookingCreationService.PhoneText);
            guestKind = StayActorKind.Customer;
        }
        else
        {
            if (captcha.IsEnforced)
            {
                if (string.IsNullOrEmpty(dto.CaptchaToken)) return Bad("Подтвердите, что вы не робот");
                if (!await captcha.ValidateAsync(dto.CaptchaToken, remoteIp)) return Bad("Подтвердите, что вы не робот");
            }
            if (!TryPhone(dto.GuestPhone, out canonicalPhone)) return Bad(StayBookingCreationService.PhoneText);
            guestKind = StayActorKind.Guest;
        }

        // 6. The gate: the executor is mandatory always (ЮР39-2); the requisites only with a prepayment.
        var prepayPercent = service.StandalonePrepayPercent;
        var gate = await companyService.EvaluateGateAsync(company, settings, prepayPercent ?? 0, ct);
        if (!gate.Accepting) return Refuse(ServiceRefusalCode.NotAcceptingBookings, ServiceTexts.NotAcceptingGuest, gate.ReasonCode);

        var now = clock.UtcNow;
        var notifyByMessenger = dto.NotifyByMessenger && settings.GuestMessengerEnabled && await channelReader.IsMessengerAvailableAsync(company.Id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // 7. The number's limits, under the number's lock (lock order: phone → [house →] service, §39.5.2).
        await AdvisoryLock.AcquireAsync(db, $"stay-guest-phone:{canonicalPhone}");
        var twin = await db.StayServiceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == company.Id && o.IdempotencyKey == idempotencyKey, ct);
        if (twin is not null) return new ServiceOrderCreateResult(null, twin, Created: false);
        switch (await throttle.CheckAsync(company.Id, canonicalPhone, now, ct))
        {
            case StayThrottleVerdict.TooManyHeld: return Throttled(ServiceWording.For(company.Kind).TooManyHeldOrders);
            case StayThrottleVerdict.TooManyPerDay: return Throttled(ServiceWording.For(company.Kind).TooManyOrdersPerDay);
        }

        // 8. The lock of the service: lazy release → the rules (counting what is still active) → the money.
        await sessionWriter.LockServiceAsync(service.Id);
        await holdReleaser.ReleaseExpiredAsync(scope, selection.BusinessDate, ct);
        var evaluation = await slots.EvaluateAsync(scope, selection, staff: false, stay: null, includeExpiredHolds: true, extraOccupied: null, prepayPercent, ct);
        if (!evaluation.Ok) return Refuse(evaluation.Problems[0].Code, evaluation.Problems[0].Message);
        var money = evaluation.Money!;
        if (money.TotalRub != dto.ExpectedTotalRub)
        {
            var quote = ServiceQuoteBuilder.Build(scope, evaluation, prepayPercent, gate, settings.HoldMinutes);
            return new ServiceOrderCreateResult(new ConflictObjectResult(new ServiceRefusalDto(ServiceRefusalCode.PriceChanged, ServiceTexts.PriceChanged(money.TotalRub), null, quote)));
        }

        // 9. The order with every snapshot, its one session and the first line of the journal.
        var held = money.PrepayRub > 0;
        var order = NewOrder(scope, evaluation, money, prepayPercent ?? 0, now);
        order.IdempotencyKey = idempotencyKey;
        order.Status = held ? StayBookingStatus.Held : StayBookingStatus.Confirmed;
        order.HoldExpiresAtUtc = held ? now.AddMinutes(settings.HoldMinutes) : null;
        order.GuestKind = guestKind;
        order.GuestUserId = guestKind == StayActorKind.Customer ? account!.Id : null;
        order.GuestName = guestName;
        order.GuestPhone = canonicalPhone;
        order.Comment = comment;
        order.NotifyByMessenger = notifyByMessenger;
        order.MessengerConsentVersion = notifyByMessenger ? legalProvider.Current?.GetText(LegalTextKey.StayMessengerConsent)?.Version : null;
        order.MessengerConsentAtUtc = notifyByMessenger ? now : null;
        order.PaymentDetailsSnapshot = held ? settings.PaymentDetails : null;
        order.PaymentPurposeSnapshot = held ? settings.PaymentPurpose : null;
        ApplyVersions(order, guestKind, now);

        db.StayServiceOrders.Add(order);
        var actor = await actors.ResolveGuestAsync(user, guestName, ct);
        sessionWriter.Add(new NewServiceSession(company.Id, service, null, order.Id, evaluation, guestKind, actor.UserId, actor.NameSnapshot, null, null, null), now);
        await eventLog.AppendAsync(order, StayServiceOrderEventKind.Created, actor, null, order.Status);

        // 10. Save. A race with another session is the database's EXCLUDE constraint; a race of the same key is the unique index.
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ServiceSessionWriter.IsOverlapViolation(ex))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Refuse(ServiceRefusalCode.SlotTaken, ServiceTexts.SlotTaken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == IdempotencyIndex)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.StayServiceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == company.Id && o.IdempotencyKey == idempotencyKey, ct);
            if (winner is null) throw;
            return new ServiceOrderCreateResult(null, winner, Created: false);
        }
        return new ServiceOrderCreateResult(null, order, Created: true);
    }

    // ── a manual order (staff, P1) ──

    public async Task<ServiceOrderCreateResult> CreateManualAsync(CompanyKind kind, Guid companyId, ManualServiceOrderInput dto, StayActor actor, CancellationToken ct)
    {
        if (dto.ServiceId is null) return new ServiceOrderCreateResult(new NotFoundResult());
        var formError = ValidateSelection(dto.BusinessDate, dto.StartMinute, dto.Hours, dto.Items, out var selection);
        if (formError is not null) return Bad(formError);
        var guestName = (dto.GuestName ?? string.Empty).Trim();
        if (guestName.Length == 0) return Bad("Укажите имя");
        if (guestName.Length > 100) return Bad("Имя — не длиннее 100 символов");
        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        if (comment is { Length: > 500 }) return Bad("Комментарий — не длиннее 500 символов");
        if (dto.RequestBasis is null || !Enum.IsDefined(dto.RequestBasis.Value)) return Bad(ServiceTexts.BasisRequired);
        if (dto.IdempotencyKey is null || dto.IdempotencyKey == Guid.Empty) return Bad("Нужен ключ запроса — обновите страницу");
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(dto.GuestPhone))
        {
            if (!TryPhone(dto.GuestPhone, out var canonical)) return Bad(StayBookingCreationService.PhoneText);
            phone = canonical;
        }

        var scope = await slots.FindOfCompanyAsync(kind, companyId, dto.ServiceId.Value, ct);
        if (scope is null || scope.Service.ArchivedAtUtc is not null) return new ServiceOrderCreateResult(new NotFoundResult());
        var (service, company, _) = scope;
        var existing = await db.StayServiceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.IdempotencyKey == dto.IdempotencyKey, ct);
        if (existing is not null) return new ServiceOrderCreateResult(null, existing, Created: false);

        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await sessionWriter.LockServiceAsync(service.Id);
        await holdReleaser.ReleaseExpiredAsync(scope, selection.BusinessDate, ct);
        var evaluation = await slots.EvaluateAsync(scope, selection, staff: true, stay: null, includeExpiredHolds: true, extraOccupied: null, prepayPercent: null, ct);
        if (!evaluation.Ok) return Refuse(evaluation.Problems[0].Code, evaluation.Problems[0].Message);

        var order = NewOrder(scope, evaluation, evaluation.Money!, 0, now);
        order.IdempotencyKey = dto.IdempotencyKey.Value;
        order.IsManual = true;
        order.Status = StayBookingStatus.Confirmed;
        order.GuestKind = StayActorKind.Staff;
        order.GuestName = guestName;
        order.GuestPhone = phone;
        order.Comment = comment;
        order.RequestBasis = dto.RequestBasis;
        // No messenger consent snapshot exists for a guest the staff typed in (Т37-12: the tick is the guest's own) — so no messenger notices, as for a manual booking.
        order.NotifyByMessenger = false;
        ApplyVersions(order, StayActorKind.Staff, now);
        db.StayServiceOrders.Add(order);
        sessionWriter.Add(new NewServiceSession(company.Id, service, null, order.Id, evaluation, actor.Kind, actor.UserId, actor.NameSnapshot, dto.RequestBasis, null, null), now);
        await eventLog.AppendAsync(order, StayServiceOrderEventKind.Created, actor, null, order.Status);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ServiceSessionWriter.IsOverlapViolation(ex))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Refuse(ServiceRefusalCode.SlotTaken, ServiceTexts.SlotTaken);
        }
        return new ServiceOrderCreateResult(null, order, Created: true);
    }

    // ── helpers ──

    private StayServiceOrder NewOrder(ServiceScope scope, ServiceEvaluation e, ServiceQuoteResult money, int prepayPercent, DateTime now)
    {
        var (service, company, settings) = scope;
        return new StayServiceOrder
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, ServiceId = service.Id, PublicToken = PublicStayToken.Generate(), Version = 1,
            ServiceAmountRub = money.ServiceAmountRub, ItemsAmountRub = money.ItemsAmountRub, TotalRub = money.TotalRub, PrepayPercentSnapshot = prepayPercent,
            PrepayRub = money.PrepayRub, DueOnSiteRub = money.DueOnSiteRub, CancellationPolicySnapshot = service.CancellationPolicy,
            CancellationBoundaryHoursSnapshot = service.CancellationBoundaryHours, TimeZoneIdSnapshot = company.TimeZoneId,
            ProviderSnapshotJson = StaysCompanyService.ProviderSnapshotJson(settings), CreatedAtUtc = now, UpdatedAtUtc = now,
        };
    }

    private void ApplyVersions(StayServiceOrder order, StayActorKind guestKind, DateTime now)
    {
        var snapshot = legalProvider.Current;
        order.BookingNoticeVersion = snapshot?.GetText(LegalTextKey.StayServiceBookingNotice)?.Version;
        order.BookingTermsVersion = snapshot?.GetText(LegalTextKey.StayServiceBookingTerms)?.Version;
        order.CancellationTermsVersion = snapshot?.GetText(LegalTextKey.StayServiceCancellationTerms)?.Version;
        // A guest's consent snapshot exactly like a house booking: the versions in force right now, from the server.
        if (guestKind != StayActorKind.Guest) return;
        var privacy = snapshot?.Get(LegalDocumentType.Privacy);
        var terms = snapshot?.Get(LegalDocumentType.TermsClient);
        if (privacy is null || terms is null) return;
        order.ConsentPrivacyVersion = privacy.Version;
        order.ConsentTermsVersion = terms.Version;
        order.ConsentAcceptedAtUtc = now;
    }

    private static bool TryPhone(string? raw, out string canonical)
    {
        canonical = string.Empty;
        return !string.IsNullOrWhiteSpace(raw) && PhoneNormalizer.TryNormalizeRussian(raw, out canonical);
    }

    private static ServiceOrderCreateResult Bad(string message) => new(new BadRequestObjectResult(message));

    private static ServiceOrderCreateResult Throttled(string message) => new(new ObjectResult(message) { StatusCode = StatusCodes.Status429TooManyRequests });

    private static ServiceOrderCreateResult Refuse(ServiceRefusalCode code, string message, NotAcceptingReason? reason = null) =>
        new(new ConflictObjectResult(new ServiceRefusalDto(code, message, reason?.ToString())));
}
