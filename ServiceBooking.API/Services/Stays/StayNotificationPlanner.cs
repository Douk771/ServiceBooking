using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.StaffMax;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.12.1 — the single point deciding who is told what: the table is the pure <see cref="StayNotificationPlan"/>; this class adds the
/// switches (company push, MAX, guest web-push/messenger), the showcase/demo guard and queues rows. It never calls SaveChanges.
/// </summary>
public class StayNotificationPlanner(
    AppDbContext db, StayStaffPushQueue staffPush, StayStaffMaxQueue staffMax, StayGuestPushQueue guestPush, StayMessageScheduler messenger,
    PublicSiteLinks links, IOptions<DemoModeOptions> demo, IOptions<WebPushOptions> webPush, StaffMaxAvailability maxAvailability, IStaysClock clock)
{
    public virtual async Task OnEventAsync(StayBooking booking, StayBookingEvent ev, CancellationToken ct = default)
    {
        var entries = StayNotificationPlan.ForEvent(ev.Kind, booking.IsManual).ToList();
        // Only the FIRST proof notifies the staff.
        if (ev.Kind == StayBookingEventKind.PaymentProofUploaded && ev.DetailsJson is { } d && !d.Contains("\"proofNumber\":1")) return;
        if (entries.Count == 0) return;
        await DeliverAsync(booking, ev.Id.ToString(), entries, ct);
    }

    public async Task OnScheduledAsync(StayBooking booking, StayScheduledKind kind, CancellationToken ct = default) =>
        await DeliverAsync(booking, kind.ToString(), [StayNotificationPlan.ForScheduled(kind)], ct);

    private async Task DeliverAsync(StayBooking booking, string marker, IReadOnlyList<PlannedNotification> entries, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == booking.CompanyId, ct);
        // The second, "given" lock of the demo and of showcase companies (§35.6.2): nothing is queued for anybody.
        if (ShowcaseOutboundGuard.IsSuppressed(company, demo.Value.Enabled)) return;
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == booking.CompanyId, ct) ?? new StaysSettings { CompanyId = booking.CompanyId };
        var notification = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == booking.CompanyId, ct);
        var house = await db.Houses.AsNoTracking().FirstAsync(h => h.Id == booking.HouseId, ct);
        var staffPushOn = notification?.StaffPushEnabled ?? new CompanyNotificationSettings().StaffPushEnabled;
        var maxOn = settings.StaffMaxEnabled && maxAvailability.Enabled;
        var platformPush = string.Equals(webPush.Value.Provider, "web-push", StringComparison.OrdinalIgnoreCase);
        var facts = BuildFacts(booking, company, house, settings);

        foreach (var entry in entries)
        {
            var type = entry.Type;
            if (entry.Audience == StayAudience.Staff)
            {
                var url = links.StaysCabinetBookingUrl(booking.CompanyId, booking.Id);
                if (staffPushOn) await staffPush.QueueAsync(booking, ParseId(marker), type, StayNotificationTexts.StaffPush(type, facts, booking.Id, url), ct);
                if (maxOn) await staffMax.QueueAsync(booking, ParseId(marker), type, StayNotificationTexts.StaffMax(type, facts, booking.Id, url), ct);
                continue;
            }
            if (settings.GuestWebPushEnabled && platformPush && type != NotificationType.StayGuestCreated)
                await guestPush.QueueAsync(booking, marker, type, StayNotificationTexts.GuestPush(type, booking.Id, booking.PublicToken), ct);
            if (booking.NotifyByMessenger && settings.GuestMessengerEnabled)
                await messenger.QueueAsync(booking, company, marker, type, facts, ct);
        }
    }

    private static Guid ParseId(string marker) => Guid.TryParse(marker, out var id) ? id : Guid.NewGuid();

    private StayTextFacts BuildFacts(StayBooking b, Company company, House house, StaysSettings settings)
    {
        var holdLocal = b.HoldExpiresAtUtc is { } h ? StayTime.LocalDateTime(b.TimeZoneIdSnapshot, h) : (DateTime?)null;
        var full = settings.CheckInInfoSendFullText
            ? string.Join("\n", new[] { settings.CheckInInfoText, house.CheckInInfoText }.Where(t => !string.IsNullOrWhiteSpace(t))) : null;
        return new StayTextFacts(company.Name, house.Name, b.CheckInDate, b.CheckOutDate, b.Nights, b.PrepayRub, b.DueAtCheckInRub, house.Address ?? company.Address,
            company.Phone, b.StatusReason, b.PaymentDetailsSnapshot, b.PaymentPurposeSnapshot, holdLocal, StayFormat.Time(b.CheckInTimeSnapshot), string.IsNullOrEmpty(full) ? null : full);
    }
}
