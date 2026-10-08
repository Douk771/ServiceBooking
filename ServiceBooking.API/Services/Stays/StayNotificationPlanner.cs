using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Orders.Notifications;
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
    PublicSiteLinks links, IOptions<DemoModeOptions> demo, IOptions<WebPushOptions> webPush, StaffMaxAvailability maxAvailability, ArrivalReminderService reminders)
{
    public virtual async Task OnEventAsync(StayBooking booking, StayBookingEvent ev, CancellationToken ct = default)
    {
        var details = ev.DetailsJson ?? string.Empty;
        var entries = StayNotificationPlan.ForEvent(ev.Kind, booking.IsManual, sessionByStaff: details.Contains("\"addedByStaff\":true"), viaBooking: details.Contains("\"viaBooking\":true")).ToList();
        // Only the FIRST proof notifies the staff.
        if (ev.Kind == StayBookingEventKind.PaymentProofUploaded && ev.DetailsJson is { } d && !d.Contains("\"proofNumber\":1")) return;
        if (entries.Count == 0) return;
        if (ev.ServiceSessionId is { } sessionId)
        {
            await DeliverSessionAsync(booking, sessionId, ev, entries, ct);
            return;
        }
        var m = System.Text.RegularExpressions.Regex.Match(details, "\"services\":(\\d+)");
        await DeliverAsync(booking, ev.Id.ToString(), entries, ct, m.Success ? int.Parse(m.Groups[1].Value) : 0);
    }

    // ── cycle 39: sessions of services ──

    private async Task<StayServiceSession?> SessionAsync(Guid id, CancellationToken ct) =>
        db.StayServiceSessions.Local.FirstOrDefault(x => x.Id == id) ?? await db.StayServiceSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    private ServiceTextFacts ServiceFacts(Company company, StayServiceSession session, string? houseName, string? reason, StayServiceOrder? order, StaysSettings settings, bool paid = false)
    {
        var holdLocal = order?.HoldExpiresAtUtc is { } h ? StayTime.LocalDateTime(order.TimeZoneIdSnapshot, h) : (DateTime?)null;
        return new ServiceTextFacts(company.Name, session.ServiceNameSnapshot, houseName, session.BusinessDate, session.StartMinute, session.Hours, company.Phone, reason,
            order?.PrepayRub ?? 0, paid, order?.PaymentDetailsSnapshot, order?.PaymentPurposeSnapshot, holdLocal, company.Address);
    }

    private async Task DeliverSessionAsync(StayBooking booking, Guid sessionId, StayBookingEvent ev, IReadOnlyList<PlannedNotification> entries, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == booking.CompanyId, ct);
        if (ShowcaseOutboundGuard.IsSuppressed(company, demo.Value.Enabled)) return;
        var session = await SessionAsync(sessionId, ct);
        if (session is null) return;
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == booking.CompanyId, ct) ?? new StaysSettings { CompanyId = booking.CompanyId };
        var house = await db.Houses.AsNoTracking().FirstAsync(h => h.Id == booking.HouseId, ct);
        var facts = ServiceFacts(company, session, house.Name, ev.Reason, null, settings);
        await DeliverCoreAsync(StayNotificationSubject.Of(booking), company, settings, booking.NotifyByMessenger, booking.GuestPhone, booking.GuestName, booking.GuestUserId,
            booking.PersonalDataErased, ev.Id.ToString(), entries, sessionId, facts, links.StayBookingPageUrl(booking.PublicToken), $"/b/{booking.PublicToken}", booking.Id, forOrder: false, ct);
    }

    public virtual async Task OnOrderEventAsync(StayServiceOrder order, StayServiceOrderEvent ev, CancellationToken ct = default)
    {
        var entries = StayNotificationPlan.ForOrderEvent(ev.Kind, order.IsManual).ToList();
        if (ev.Kind == StayServiceOrderEventKind.PaymentProofUploaded && ev.DetailsJson is { } d && !d.Contains("\"proofNumber\":1")) return;
        if (entries.Count == 0) return;
        await DeliverOrderAsync(order, ev.Id.ToString(), entries, ct, paid: ev.FromStatus is StayBookingStatus.AwaitingPaymentCheck or StayBookingStatus.Confirmed);
    }

    public async Task OnOrderScheduledAsync(StayServiceOrder order, CancellationToken ct = default) =>
        await DeliverOrderAsync(order, "HoldExpiring", [new PlannedNotification(NotificationType.ServiceGuestHoldExpiring, StayAudience.Guest)], ct);

    private async Task DeliverOrderAsync(StayServiceOrder order, string marker, IReadOnlyList<PlannedNotification> entries, CancellationToken ct, bool paid = false)
    {
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == order.CompanyId, ct);
        if (ShowcaseOutboundGuard.IsSuppressed(company, demo.Value.Enabled)) return;
        var session = db.StayServiceSessions.Local.FirstOrDefault(x => x.StayServiceOrderId == order.Id)
            ?? await db.StayServiceSessions.AsNoTracking().FirstOrDefaultAsync(x => x.StayServiceOrderId == order.Id, ct);
        if (session is null) return;
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == order.CompanyId, ct) ?? new StaysSettings { CompanyId = order.CompanyId };
        var facts = ServiceFacts(company, session, null, order.StatusReason, order, settings, paid);
        await DeliverCoreAsync(StayNotificationSubject.Of(order), company, settings, order.NotifyByMessenger, order.GuestPhone, order.GuestName, order.GuestUserId,
            order.PersonalDataErased, marker, entries, session.Id, facts, links.StayServiceOrderPageUrl(order.PublicToken), $"/s/{order.PublicToken}", order.Id, forOrder: true, ct);
    }

    private async Task DeliverCoreAsync(
        StayNotificationSubject subject, Company company, StaysSettings settings, bool notifyByMessenger, string? phone, string? name, string? userId, bool erased,
        string marker, IReadOnlyList<PlannedNotification> entries, Guid sessionId, ServiceTextFacts facts, string pageUrl, string guestRelativeUrl, Guid subjectId, bool forOrder,
        CancellationToken ct)
    {
        var notification = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct);
        var staffPushOn = notification?.StaffPushEnabled ?? new CompanyNotificationSettings().StaffPushEnabled;
        var maxOn = settings.StaffMaxEnabled && maxAvailability.Enabled;
        var platformPush = string.Equals(webPush.Value.Provider, "web-push", StringComparison.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var type = entry.Type;
            if (entry.Audience == StayAudience.Staff)
            {
                var url = links.StaysCabinetServiceSessionUrl(company.Id, sessionId);
                if (staffPushOn) await staffPush.QueueAsync(subject, ParseId(marker), type, ServiceNotificationTexts.StaffPush(type, facts, sessionId, url), ct);
                if (maxOn) await staffMax.QueueAsync(subject, ParseId(marker), type, ServiceNotificationTexts.StaffMax(type, facts, sessionId, url), ct);
                continue;
            }
            if (settings.GuestWebPushEnabled && platformPush && type is not (NotificationType.ServiceGuestOrderCreated or NotificationType.StayGuestCreated))
                await guestPush.QueueAsync(subject, marker, type, ServiceNotificationTexts.GuestPush(type, subjectId, guestRelativeUrl, forOrder), ct);
            if (notifyByMessenger && settings.GuestMessengerEnabled)
                await messenger.QueueAsync(subject, phone, name, userId, erased, company, marker, type, unsub => ServiceNotificationTexts.Messenger(type, facts, pageUrl, unsub), ct);
        }
    }

    public async Task OnScheduledAsync(StayBooking booking, StayScheduledKind kind, CancellationToken ct = default) =>
        await DeliverAsync(booking, kind.ToString(), [StayNotificationPlan.ForScheduled(kind)], ct);

    private async Task DeliverAsync(StayBooking booking, string marker, IReadOnlyList<PlannedNotification> entries, CancellationToken ct, int servicesCount = 0)
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
        var facts = BuildFacts(booking, company, house, settings) with { ServicesCount = servicesCount };

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
            if (type == NotificationType.StayGuestArrivalReminder)
            {
                // ARCHITECTURE_CYCLE39.md §39.11: the owner's template (NULL = the text of cycle 37 byte for byte); the push carries the template only when the owner switched it on.
                var reminderFacts = await reminders.FactsAsync(booking, company, unsubscribeUrl: null, ct);
                if (settings.GuestWebPushEnabled && platformPush)
                {
                    var push = ArrivalReminderTemplate.Render(settings.ArrivalReminderTemplate, reminderFacts, ReminderMode.Push, settings.ArrivalReminderPushText);
                    await guestPush.QueueAsync(booking, marker, type, new PushPayload(StayNotificationTexts.GuestPushTitle, push.Text, $"sg-{booking.Id}", $"/b/{booking.PublicToken}"), ct);
                }
                if (booking.NotifyByMessenger && settings.GuestMessengerEnabled)
                    await messenger.QueueAsync(StayNotificationSubject.Of(booking), booking.GuestPhone, booking.GuestName, booking.GuestUserId, booking.PersonalDataErased, company, marker, type,
                        unsub => ArrivalReminderTemplate.Render(settings.ArrivalReminderTemplate, reminderFacts with { UnsubscribeUrl = unsub }, ReminderMode.Messenger).Text, ct);
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
