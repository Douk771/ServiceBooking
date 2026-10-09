using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.12.3 — queues a messenger message to the guest of a booking through the SAME gate, routing and funding as every other message:
/// opt-out, the paid numbers of the account, the channel, the provider-delivery consent. Never sends by itself; `VisitStartUtc` of the row is the moment it is
/// outdated (queued + <c>Stays:GuestMessageTtlMinutes</c>) — the dispatcher expires it as <c>StayMessageOutdated</c>.
/// </summary>
public sealed class StayMessageScheduler(
    AppDbContext db, AccountMessagingReader messagingReader, MessengerConsentResolver consentResolver, PublicSiteLinks links,
    IOptions<NotificationOptions> notificationOptions, IOptions<StaysOptions> options, IStaysClock clock)
{
    public async Task QueueAsync(StayBooking booking, Company company, string marker, NotificationType type, StayTextFacts facts, CancellationToken ct)
    {
        var phone = booking.GuestPhone;
        if (string.IsNullOrEmpty(phone) || booking.PersonalDataErased) return; // depersonalised: nobody to write to

        var now = clock.UtcNow;
        var outdatedAtUtc = now.AddMinutes(options.Value.GuestMessageTtlMinutes);
        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct);
        // ARCHITECTURE_CYCLE40.md §40.4/§40.5.5: the company's numbers are those of its billing account.
        var messaging = await messagingReader.ForCompanyAsync(company.Id, ct: ct);
        var optedOut = await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == phone, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — dispatch-time check against the message's own recipient phone (the booking's), not an account reading another subject's data

        var priorityTransport = settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;

        // Consent (a house booking always carries a bool mark) → the cycle-40 gate → routing over the routable transports.
        var consent = await consentResolver.DecideAsync(booking.NotifyByMessenger, booking.GuestUserId, phone, ct);
        var decision = MessagingQueueing.Decide(type, messaging, settings, optedOut, consent, now, outdatedAtUtc);
        if (decision.IsSkipped)
        {
            await AddIfNew(booking, company, marker, type, string.Empty, priorityTransport, decision.RepresentativeChannelId, outdatedAtUtc,
                NotificationStatus.Skipped, decision.SkipReason, ct);
            return;
        }

        var body = StayNotificationTexts.Messenger(type, facts, links.StayBookingPageUrl(booking.PublicToken), UnsubscribeUrl(phone));
        foreach (var target in decision.Targets)
            await AddIfNew(booking, company, marker, type, body, target.Transport, target.ChannelId, outdatedAtUtc, NotificationStatus.Pending, null, ct);
    }

    private string? UnsubscribeUrl(string phone)
    {
        var key = notificationOptions.Value.UnsubscribeKey;
        return string.IsNullOrEmpty(key) ? null : links.UnsubscribeUrl(UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(key)));
    }

    private async Task AddIfNew(
        StayBooking booking, Company company, string marker, NotificationType type, string body, NotificationTransport transport, Guid? channelId,
        DateTime outdatedAtUtc, NotificationStatus status, NotificationReason? reason, CancellationToken ct)
    {
        // One event/marker — one message per channel: the key is the whole guarantee (unique index), the existence check only avoids an exception.
        var key = $"{type}:stay:{marker}:{transport}";
        if (await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == key, ct)) return;
        db.OutboundNotifications.Add(new OutboundNotification
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, ChannelId = channelId, Transport = transport, StayBookingId = booking.Id, Type = type,
            RecipientPhone = booking.GuestPhone!, RecipientName = booking.GuestName, RecipientUserId = booking.GuestUserId, Body = body,
            DueAtUtc = clock.UtcNow, VisitStartUtc = outdatedAtUtc, Status = status, Reason = reason, Generation = 0, IdempotencyKey = key,
        });
    }
}
