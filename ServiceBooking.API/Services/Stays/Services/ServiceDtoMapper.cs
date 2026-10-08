using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.2–§39.10, API_CONTRACT_CYCLE39.md §39.23, §39.24, §39.29 — a session and an order through the guest's eyes and through the staff's. Every sentence,
/// status, refund and available action is assembled HERE (the frontend only prints). A guest sees the requisites and the executor's full details only from their own link;
/// the staff's card is the only place with a full phone number.
/// </summary>
public class ServiceDtoMapper(
    AppDbContext db, IOptions<StaysOptions> options, IOptions<WebPushOptions> webPush, IStaysClock clock, StaysCompanyService companyService)
{
    // ── shared pieces ──

    public static List<SessionItemDto> ItemsOf(StayServiceSession s) =>
        ServiceJson.ReadItems(s.ItemsJson).Select(i => new SessionItemDto(i.Name, i.UnitPriceRub, i.Quantity, i.AmountRub)).ToList();

    public static List<HourPriceDto> HourPricesOf(StayServiceSession s) =>
        ServiceJson.ReadHourPrices(s.HourPricesJson).Select(h => new HourPriceDto(h.StartMinute, ServiceTimeFormat.GuestMoment(s.BusinessDate, h.StartMinute), h.PriceRub)).ToList();

    public static List<ServiceChargeLineDto> LinesOf(StayServiceSession s) =>
        ServiceQuoteBuilder.Lines(s.ServiceNameSnapshot, s.Hours, s.ServiceAmountRub,
            ServiceJson.ReadItems(s.ItemsJson).Select(i => new ResolvedItem(i.ItemId, i.Name, i.UnitPriceRub, i.Quantity, i.Quantity)));

    /// <summary>The time of a session. <paramref name="forStaff"/> switches the label to the form of SPEC §4.9 (the business date of the start).</summary>
    public static ServiceTimeDto TimeOf(StayServiceSession s, bool forStaff = false) =>
        new(s.BusinessDate, s.StartMinute, s.StartMinute + 60 * s.Hours, s.Hours, s.StartUtc, s.EndUtc,
            forStaff ? ServiceTimeFormat.Staff(s.BusinessDate, s.StartMinute, s.Hours) : ServiceTimeFormat.Guest(s.BusinessDate, s.StartMinute, s.Hours));

    public static string PreparedUntilLabel(StayServiceSession s) =>
        ServiceTimeFormat.StaffMoment(s.BusinessDate, s.StartMinute + 60 * s.Hours + s.BufferMinutesSnapshot);

    public static bool IsStaffKind(StayActorKind kind) => kind is StayActorKind.Staff or StayActorKind.SuperAdmin;

    public static string DisplayStatusOf(StayServiceOrder o, DateTime endUtc, DateTime nowUtc) =>
        o.Status == StayBookingStatus.Confirmed && nowUtc >= endUtc ? "Completed" : o.Status.ToString();

    public string ServiceUrl(Company company, string serviceSlug) => $"/{company.Slug}/uslugi/{serviceSlug}";

    // ── the order for the guest ──

    public async Task<PublicServiceOrderDto> ToPublicOrderAsync(StayServiceOrder o, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var session = await db.StayServiceSessions.AsNoTracking().FirstAsync(s => s.StayServiceOrderId == o.Id, ct);
        var service = await db.StayServices.AsNoTracking().FirstAsync(s => s.Id == o.ServiceId, ct);
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == o.CompanyId, ct);
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == o.CompanyId, ct) ?? new StaysSettings { CompanyId = o.CompanyId };
        var cover = await db.StayServicePhotos.AsNoTracking().Where(p => p.ServiceId == service.Id).OrderBy(p => p.Position).Select(p => p.ThumbnailUrl ?? p.Url).FirstOrDefaultAsync(ct);
        var proofs = await db.StayPaymentProofs.AsNoTracking().Where(p => p.StayServiceOrderId == o.Id).OrderBy(p => p.UploadedAtUtc).ToListAsync(ct);

        var display = DisplayStatusOf(o, session.EndUtc, now);
        var active = !StayStateMachine.IsTerminal(o.Status);
        var holdOk = o.Status != StayBookingStatus.Held || o.HoldExpiresAtUtc > now;
        var maxProofs = options.Value.PaymentProofs.MaxPerBooking;
        var actions = new List<string>();
        if (holdOk && o.Status is StayBookingStatus.Held or StayBookingStatus.AwaitingPaymentCheck && proofs.Count < maxProofs) actions.Add("AttachProof");
        var canCancel = active && holdOk && now < session.StartUtc;
        if (canCancel) actions.Add("Cancel");
        var holdExpired = o.Status == StayBookingStatus.Held && o.HoldExpiresAtUtc <= now;

        var refund = RefundFor(o, session, now, byOwner: false);
        var pushOn = settings.GuestWebPushEnabled && string.Equals(webPush.Value.Provider, "web-push", StringComparison.OrdinalIgnoreCase) && active;
        var phone = company.Phone;
        PaymentInstructionsDto? payment = active && o.PrepayRub > 0 ? new PaymentInstructionsDto(o.PaymentDetailsSnapshot, o.PaymentPurposeSnapshot, o.PrepayRub) : null;
        return new PublicServiceOrderDto(
            o.Status, display, ServiceTexts.StatusText(display), now, o.Status == StayBookingStatus.Held ? o.HoldExpiresAtUtc : null,
            new OrderServiceRefDto(session.ServiceNameSnapshot, ServiceUrl(company, service.Slug), cover),
            new OrderCompanyRefDto(company.Name, phone, $"/{company.Slug}", company.Address, company.YandexMapsUrl, company.TwoGisUrl),
            StaysCompanyService.ProviderFromSnapshot(o.ProviderSnapshotJson), TimeOf(session), ItemsOf(session), LinesOf(session), HourPricesOf(session),
            o.ServiceAmountRub, o.ItemsAmountRub, o.TotalRub, o.PrepayPercentSnapshot > 0 ? o.PrepayPercentSnapshot : null, o.PrepayRub, o.DueOnSiteRub, payment,
            o.PaymentConfirmedAtUtc, proofs.Select(StayDtoMapper.ToProof).ToList(),
            new ProofRulesDto(actions.Contains("AttachProof"), maxProofs, options.Value.PaymentProofs.MaxFileBytes, StayDtoMapper.AcceptedProofTypes.ToList()),
            new OrderCancellationDto(o.CancellationPolicySnapshot, ServiceTexts.CancellationSummary(o.CancellationPolicySnapshot, o.CancellationBoundaryHoursSnapshot), canCancel,
                refund, !canCancel && active ? (holdExpired ? ServiceTexts.HoldExpired(phone) : ServiceTexts.AlreadyStarted(phone)) : null),
            o.GuestName, StayPhone.Mask(o.GuestPhone), o.Comment, o.StatusReason,
            StayStateMachine.IsTerminal(o.Status) ? (ServiceTexts.OutcomeText(o.Status, o.StatusReason, phone, o.PrepayRub) is { Length: > 0 } t ? t : null) : null,
            new OrderNotificationsDto(new WebPushInfoDto(pushOn, pushOn ? webPush.Value.VapidPublicKey : null), o.NotifyByMessenger), actions);
    }

    public ServiceRefundViewDto RefundFor(StayServiceOrder o, StayServiceSession s, DateTime nowUtc, bool byOwner)
    {
        var firstHour = ServiceJson.ReadHourPrices(s.HourPricesJson).FirstOrDefault()?.PriceRub ?? 0;
        var r = ServiceRefund.Compute(o.Status, o.CancellationPolicySnapshot, o.CancellationBoundaryHoursSnapshot, o.PrepayRub, firstHour, s.StartUtc, nowUtc, byOwner,
            options.Value.Services.MaxDeductionHours);
        return new ServiceRefundViewDto(r.Kind, r.RefundAtLeastRub, r.MaxDeductionRub, r.Text);
    }

    // ── the sessions on a booking's page ──

    public async Task<List<PublicBookingSessionDto>> PublicSessionsOfAsync(StayBooking b, Company company, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var sessions = await db.StayServiceSessions.AsNoTracking().Where(s => s.StayBookingId == b.Id).OrderBy(s => s.StartUtc).ToListAsync(ct);
        if (sessions.Count == 0) return [];
        var ids = sessions.Select(s => s.ServiceId).Distinct().ToList();
        var slugs = await db.StayServices.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Slug, ct);
        var bookingActive = !StayStateMachine.IsTerminal(b.Status);
        return sessions.Select(s =>
        {
            var staff = IsStaffKind(s.AddedByKind);
            var canCancel = bookingActive && s.State == StayServiceSessionState.Active && now < s.StartUtc;
            return new PublicBookingSessionDto(
                s.Id, s.ServiceNameSnapshot, slugs.TryGetValue(s.ServiceId, out var slug) ? ServiceUrl(company, slug) : null, TimeOf(s), ItemsOf(s), s.TotalRub, s.State,
                ServiceTexts.SessionStateText(s.State), canCancel,
                bookingActive && s.State == StayServiceSessionState.Active && !canCancel ? ServiceTexts.AlreadyStarted(company.Phone) : null,
                staff, staff && s.State == StayServiceSessionState.Active ? ServiceTexts.AddedByStaffGuestText : null, s.StatusReason);
        }).ToList();
    }

    /// <summary>The block «Услуги к проживанию»: can a guest add one, and if not — why (API_CONTRACT_CYCLE39.md §39.24.1). The gate is evaluated only when it can matter.</summary>
    public async Task<BookingServicesBlockDto> ServicesBlockAsync(StayBooking b, Company company, StaysSettings settings, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        string? reason = null;
        if (StayStateMachine.IsTerminal(b.Status)) reason = ServiceTexts.BookingFinished;
        else if (now >= StayTime.ToUtc(b.TimeZoneIdSnapshot, b.CheckOutDate, b.CheckOutTimeSnapshot)) reason = ServiceTexts.CheckOutPassed;
        else if (await db.StayServiceSessions.AsNoTracking().CountAsync(s => s.StayBookingId == b.Id && s.State == StayServiceSessionState.Active, ct) >= options.Value.Services.MaxSessionsPerBooking)
            reason = ServiceTexts.TooManySessions(options.Value.Services.MaxSessionsPerBooking);
        else if (!await db.StayServices.AsNoTracking().AnyAsync(s => s.CompanyId == b.CompanyId && s.IsPublished && s.ArchivedAtUtc == null && s.AvailableForHouseBookings, ct))
            return new BookingServicesBlockDto(false, null, null);
        else if (!(await companyService.EvaluateGateAsync(company, settings, 0, ct)).Accepting) reason = ServiceTexts.NotAcceptingGuest;
        return new BookingServicesBlockDto(reason is null, reason, reason is null && b.Status == StayBookingStatus.Held ? ServiceTexts.HeldHint : null);
    }

    public async Task<ArrivalReminderSnapshotDto?> ReminderSnapshotOf(StayBooking b) =>
        await Task.FromResult(b.ArrivalReminderPageText is { } text && b.ArrivalReminderSentAtUtc is { } sent ? new ArrivalReminderSnapshotDto(text, sent) : null);

    // ── the staff's views ──

    public async Task<List<StaffBookingSessionDto>> StaffSessionsOfAsync(StayBooking b, CancellationToken ct = default)
    {
        var sessions = await db.StayServiceSessions.AsNoTracking().Where(s => s.StayBookingId == b.Id).OrderBy(s => s.StartUtc).ToListAsync(ct);
        var now = clock.UtcNow;
        var bookingActive = !StayStateMachine.IsTerminal(b.Status);
        return sessions.Select(s => new StaffBookingSessionDto(
            s.Id, s.Version, s.ServiceNameSnapshot, TimeOf(s, forStaff: true), ItemsOf(s), s.TotalRub, s.State, ServiceTexts.SessionStateText(s.State), AddedByText(s),
            bookingActive && s.State == StayServiceSessionState.Active, s.StatusReason)).ToList();
    }

    public static string AddedByText(StayServiceSession s) => IsStaffKind(s.AddedByKind)
        ? $"Добавлено сотрудником {s.AddedByNameSnapshot ?? "компании"}" + (s.RequestBasis is { } basis ? $" по просьбе гостя ({ServiceTexts.BasisText(basis)})" : string.Empty)
        : "Добавил гость по ссылке";

    public async Task<StaffServiceSessionCardDto> ToStaffCardAsync(StayServiceSession s, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == s.CompanyId, ct);
        StayServiceOrder? order = s.StayServiceOrderId is { } oid ? await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == oid, ct) : null;
        StayBooking? booking = s.StayBookingId is { } bid ? await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bid, ct) : null;
        string? houseName = booking is null ? null : await db.Houses.AsNoTracking().Where(h => h.Id == booking.HouseId).Select(h => h.Name).FirstAsync(ct);

        var proofs = order is null ? [] : await db.StayPaymentProofs.AsNoTracking().Where(p => p.StayServiceOrderId == order.Id).OrderBy(p => p.UploadedAtUtc).ToListAsync(ct);
        List<StayBookingEventDto> events;
        if (order is not null)
            events = (await db.StayServiceOrderEvents.AsNoTracking().Where(e => e.StayServiceOrderId == order.Id).OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Kind).ToListAsync(ct))
                .Select(e => new StayBookingEventDto(e.OccurredAtUtc, e.Kind.ToString(), ServiceTexts.StaffEventText(e.Kind), ActorText(e.ActorKind, e.ActorNameSnapshot), e.Reason)).ToList();
        else
            events = (await db.StayBookingEvents.AsNoTracking().Where(e => e.ServiceSessionId == s.Id).OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Kind).ToListAsync(ct))
                .Select(e => new StayBookingEventDto(e.OccurredAtUtc, e.Kind.ToString(), StaysTexts.EventText(e.Kind), StayDtoMapper.ActorText(e), e.Reason)).ToList();

        var actions = new List<string>();
        string statusText;
        string? display = null;
        if (order is not null)
        {
            display = DisplayStatusOf(order, s.EndUtc, now);
            statusText = ServiceTexts.StatusText(display);
            actions.AddRange(StayStateMachine.StaffActions(order.Status));
        }
        else
        {
            statusText = ServiceTexts.SessionStateText(s.State);
            if (s.State == StayServiceSessionState.Active && booking is not null && !StayStateMachine.IsTerminal(booking.Status)) actions.Add("Cancel");
        }

        var ownerRefund = order is null || StayStateMachine.IsTerminal(order.Status) || order.PrepayRub == 0 || order.Status == StayBookingStatus.Held
            ? null : ServiceTexts.OwnerCancelRefund(order.PrepayRub);
        var addedBy = new SessionAddedByDto(s.AddedByKind.ToString() is "SuperAdmin" ? "Staff" : s.AddedByKind.ToString(), s.AddedByNameSnapshot, s.RequestBasis, AddedByText(s));

        return new StaffServiceSessionCardDto(
            s.Id, order?.Version ?? s.Version, order is null ? ServiceSessionKind.InBooking : ServiceSessionKind.Standalone, s.ServiceId, s.ServiceNameSnapshot,
            TimeOf(s, forStaff: true), HourPricesOf(s), ItemsOf(s), LinesOf(s), s.ServiceAmountRub, s.ItemsAmountRub, s.TotalRub,
            order is null ? null : (order.PrepayPercentSnapshot > 0 ? order.PrepayPercentSnapshot : null), order?.PrepayRub ?? 0, order?.DueOnSiteRub ?? s.TotalRub,
            s.BufferMinutesSnapshot, PreparedUntilLabel(s), s.State, order?.Status, display, statusText,
            order is { Status: StayBookingStatus.Held } ? order.HoldExpiresAtUtc : null,
            booking is null ? null : new SessionBookingRefDto(booking.Id, houseName!, StaysTexts.StatusText(booking.Status.ToString())),
            order?.GuestName ?? booking?.GuestName, order?.GuestPhone ?? booking?.GuestPhone, order?.Comment ?? booking?.Comment, addedBy, order?.CancellationPolicySnapshot,
            ownerRefund, proofs.Select(StayDtoMapper.ToProof).ToList(),
            order?.PaymentConfirmedAtUtc is { } at ? new PaymentConfirmedDto(at, order.PaymentConfirmedByNameSnapshot ?? string.Empty) : null,
            order?.PaymentProofsPurgedAtUtc, order?.StatusReason ?? s.StatusReason, order?.IsManual ?? false, actions, events);
    }

    public static string ActorText(StayActorKind kind, string? name) => kind switch
    {
        StayActorKind.System => "Система",
        StayActorKind.Guest => "Гость",
        _ => string.IsNullOrWhiteSpace(name) ? "Сотрудник" : name!
    };
}
