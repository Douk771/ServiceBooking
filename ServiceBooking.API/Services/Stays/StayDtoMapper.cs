using System.Text.Json;
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
/// ARCHITECTURE_CYCLE37.md §37.26, §37.30 — the booking through the guest's eyes and through the staff's. Every sentence, status, refund and available action is
/// assembled HERE (the frontend only prints). The guest sees the requisites and the executor's full details only from their own link.
/// </summary>
public class StayDtoMapper(
    AppDbContext db, IOptions<StaysOptions> options, IOptions<WebPushOptions> webPush, PublicSiteLinks links, IStaysClock clock)
{
    public const string ProofMediaPdf = "application/pdf";
    public static readonly string[] AcceptedProofTypes = ["application/pdf", "image/jpeg", "image/png", "image/webp"];

    public static List<NightPriceDto> NightPricesOf(StayBooking b) =>
        JsonSerializer.Deserialize<List<NightPriceDto>>(b.NightPricesJson, StaysCompanyService.SnapshotJson) ?? [];

    public static string NightPricesJson(IEnumerable<NightPrice> prices) =>
        JsonSerializer.Serialize(prices.Select(p => new NightPriceDto(p.Date, p.PriceRub)), StaysCompanyService.SnapshotJson);

    public static StayChargeLineDto ToLine(StayBookingCharge c) => new(c.Kind, c.Label, c.Quantity, c.UnitPriceRub, c.NightsCount, c.AmountRub, c.PrepayEligible);

    public static PaymentProofDto ToProof(StayPaymentProof p) =>
        new(p.Id, p.ContentType, p.SizeBytes, p.UploadedAtUtc, p.PurgedAtUtc != null || p.StorageKey is null);

    private int FirstNightOf(StayBooking b) => NightPricesOf(b).FirstOrDefault()?.PriceRub ?? 0;

    public StayRefundView RefundFor(StayBooking b, DateTime nowUtc, bool byOwner) =>
        StayRefund.Compute(b.Status, b.CancellationPolicySnapshot, b.PrepayRub, FirstNightOf(b), b.CheckInDate, b.CheckInTimeSnapshot,
            b.TimeZoneIdSnapshot, nowUtc, byOwner, options.Value.EffectiveCancellationRules());

    public string DisplayStatusOf(StayBooking b, DateTime nowUtc) =>
        StayStateMachine.DisplayStatus(b.Status, nowUtc, b.CheckOutDate, b.CheckOutTimeSnapshot, b.TimeZoneIdSnapshot);

    // ── the guest's page ──

    public async Task<PublicStayBookingDto> ToPublicAsync(StayBooking b, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var house = await db.Houses.AsNoTracking().FirstAsync(h => h.Id == b.HouseId, ct);
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == b.CompanyId, ct);
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == b.CompanyId, ct) ?? new StaysSettings { CompanyId = b.CompanyId };
        var cover = await db.HousePhotos.AsNoTracking().Where(p => p.HouseId == house.Id).OrderBy(p => p.Position).Select(p => p.ThumbnailUrl ?? p.Url).FirstOrDefaultAsync(ct);
        var charges = await db.StayBookingCharges.AsNoTracking().Where(c => c.StayBookingId == b.Id).OrderBy(c => c.Position).ToListAsync(ct);
        var proofs = await db.StayPaymentProofs.AsNoTracking().Where(p => p.StayBookingId == b.Id).OrderBy(p => p.UploadedAtUtc).ToListAsync(ct);

        var display = DisplayStatusOf(b, now);
        var actions = StayStateMachine.GuestActions(b.Status, now, b.HoldExpiresAtUtc, b.CheckInDate, b.CheckInTimeSnapshot, b.TimeZoneIdSnapshot).ToList();
        var maxProofs = options.Value.PaymentProofs.MaxPerBooking;
        var canAttach = actions.Contains("AttachProof") && proofs.Count < maxProofs;
        if (!canAttach) actions.Remove("AttachProof");
        var canCancel = actions.Contains("Cancel");
        var refund = RefundFor(b, now, byOwner: false);
        var active = !StayStateMachine.IsTerminal(b.Status);
        // A held booking whose timer has run out but which the task has not processed yet: it cannot be cancelled because the time to pay is over.
        var holdExpired = b.Status == StayBookingStatus.Held && b.HoldExpiresAtUtc <= now;

        PaymentInstructionsDto? payment = active
            ? new PaymentInstructionsDto(b.PaymentDetailsSnapshot, b.PaymentPurposeSnapshot, b.PrepayRub) : null;
        if (b.PrepayRub == 0) payment = null;

        CheckInInfoDto? info = null;
        if (b.CheckInInfoReleasedAtUtc is not null && b.Status == StayBookingStatus.Confirmed)
            info = new CheckInInfoDto(settings.CheckInInfoText, house.CheckInInfoText);

        var pushOn = settings.GuestWebPushEnabled && string.Equals(webPush.Value.Provider, "web-push", StringComparison.OrdinalIgnoreCase) && active;
        var phone = company.Phone;
        return new PublicStayBookingDto(
            b.Status, display, StaysTexts.StatusText(display), now, b.Status == StayBookingStatus.Held ? b.HoldExpiresAtUtc : null,
            new BookingHouseRefDto(house.Name, $"/{company.Slug}/{house.Slug}", cover, house.Address ?? company.Address, house.YandexMapsUrl ?? company.YandexMapsUrl, house.TwoGisUrl ?? company.TwoGisUrl),
            new BookingCompanyRefDto(company.Name, phone, $"/{company.Slug}"),
            StaysCompanyService.ProviderFromSnapshot(b.ProviderSnapshotJson),
            b.CheckInDate, b.CheckOutDate, StayFormat.Time(b.CheckInTimeSnapshot), StayFormat.Time(b.CheckOutTimeSnapshot), b.Nights, b.Adults, b.Children,
            b.Dogs, b.NeedCot, b.ExtraBeds, StayFormat.Time(b.ArrivalTime), b.GuestName, StayPhone.Mask(b.GuestPhone), b.Comment,
            charges.Select(ToLine).ToList(), NightPricesOf(b), b.TotalRub, b.PrepayPercentSnapshot, b.PrepayRub, b.DueAtCheckInRub, payment,
            b.PaymentConfirmedAtUtc, proofs.Select(ToProof).ToList(),
            new ProofRulesDto(canAttach, maxProofs, options.Value.PaymentProofs.MaxFileBytes, AcceptedProofTypes.ToList()),
            new BookingCancellationDto(b.CancellationPolicySnapshot, StaysTexts.CancellationSummary(b.CancellationPolicySnapshot), canCancel,
                new StayRefundViewDto(refund.Kind, refund.RefundAtLeastRub, refund.MaxDeductionRub, refund.Text),
                !canCancel && active ? (holdExpired ? StaysTexts.HoldExpiredMessage(phone) : StaysTexts.CannotCancel(phone)) : null),
            b.StatusReason, StayStateMachine.IsTerminal(b.Status) ? StaysTexts.OutcomeText(b.Status, b.StatusReason, phone) is { Length: > 0 } t ? t : null : null,
            info, new BookingNotificationsDto(new WebPushInfoDto(pushOn, pushOn ? webPush.Value.VapidPublicKey : null), b.NotifyByMessenger), actions);
    }

    // ── the staff's card ──

    public async Task<StaffStayBookingCardDto> ToStaffCardAsync(StayBooking b, bool withMessages = true, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var houseName = await db.Houses.AsNoTracking().Where(h => h.Id == b.HouseId).Select(h => h.Name).FirstAsync(ct);
        var charges = await db.StayBookingCharges.AsNoTracking().Where(c => c.StayBookingId == b.Id).OrderBy(c => c.Position).ToListAsync(ct);
        var proofs = await db.StayPaymentProofs.AsNoTracking().Where(p => p.StayBookingId == b.Id).OrderBy(p => p.UploadedAtUtc).ToListAsync(ct);
        var events = await db.StayBookingEvents.AsNoTracking().Where(e => e.StayBookingId == b.Id).OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Kind).ToListAsync(ct);
        List<StayMessageLogDto>? messages = null;
        if (withMessages)
        {
            var rows = await db.OutboundNotifications.AsNoTracking().Where(n => n.StayBookingId == b.Id).OrderBy(n => n.CreatedAt).ToListAsync(ct);
            messages = rows.Select(n => new StayMessageLogDto(n.Type.ToString(), n.Transport.ToString(), n.Status.ToString(), n.CreatedAt)).ToList();
        }
        var display = DisplayStatusOf(b, now);
        var phone = await db.Companies.AsNoTracking().Where(c => c.Id == b.CompanyId).Select(c => c.Phone).FirstOrDefaultAsync(ct);
        _ = phone;
        var ownerRefund = StayStateMachine.IsTerminal(b.Status) || b.PrepayRub == 0 || b.Status == StayBookingStatus.Held
            ? null : StaysTexts.OwnerCancelRefund(b.PrepayRub);

        return new StaffStayBookingCardDto(
            b.Id, b.Version, b.Status, display, StaysTexts.StatusText(display), b.Status == StayBookingStatus.Held ? b.HoldExpiresAtUtc : null,
            new BookingHouseIdDto(b.HouseId, houseName), b.CheckInDate, b.CheckOutDate, StayFormat.Time(b.CheckInTimeSnapshot), StayFormat.Time(b.CheckOutTimeSnapshot),
            b.Nights, b.Adults, b.Children, b.Dogs, b.NeedCot, b.ExtraBeds, StayFormat.Time(b.ArrivalTime), b.GuestName, b.GuestPhone, b.GuestKind.ToString(), b.Comment,
            charges.Select(ToLine).ToList(), NightPricesOf(b), b.TotalRub, b.PrepayPercentSnapshot, b.PrepayRub, b.DueAtCheckInRub, b.CancellationPolicySnapshot,
            ownerRefund, proofs.Select(ToProof).ToList(),
            b.PaymentConfirmedAtUtc is { } at ? new PaymentConfirmedDto(at, b.PaymentConfirmedByNameSnapshot ?? string.Empty) : null,
            b.PaymentProofsPurgedAtUtc, b.StatusReason, b.IsManual, StayStateMachine.StaffActions(b.Status).ToList(),
            events.Select(e => new StayBookingEventDto(e.OccurredAtUtc, e.Kind.ToString(), StaysTexts.EventText(e.Kind), ActorText(e), e.Reason)).ToList(), messages);
    }

    public static string ActorText(StayBookingEvent e) => e.ActorKind switch
    {
        StayActorKind.System => "Система",
        StayActorKind.Guest => "Гость",
        _ => string.IsNullOrWhiteSpace(e.ActorNameSnapshot) ? "Сотрудник" : e.ActorNameSnapshot!
    };

    public string BookingUrl(StayBooking b) => links.StayBookingPageUrl(b.PublicToken);
}
