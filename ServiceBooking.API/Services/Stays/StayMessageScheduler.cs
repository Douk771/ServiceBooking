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
    public Task QueueAsync(StayBooking booking, Company company, string marker, NotificationType type, StayTextFacts facts, CancellationToken ct) =>
        QueueAsync(StayNotificationSubject.Of(booking), booking.GuestPhone, booking.GuestName, booking.GuestUserId, booking.PersonalDataErased, company, marker, type,
            unsubscribeUrl => StayNotificationTexts.Messenger(type, facts, links.StayBookingPageUrl(booking.PublicToken), unsubscribeUrl), ct, booking.NotifyByMessenger);

    /// <summary>
    /// The one path of every messenger message of «Дома» (a booking or a stand-alone order): the gate, the routing and the funding are the same; <paramref name="bodyOf"/> receives the
    /// guest's unsubscribe link (or null) and returns the text. Nothing is queued for a depersonalised subject.
    /// </summary>
    public async Task QueueAsync(
        StayNotificationSubject subject, string? phone, string? recipientName, string? recipientUserId, bool personalDataErased, Company company, string marker,
        NotificationType type, Func<string?, string> bodyOf, CancellationToken ct, bool notifyByMessenger = true)
    {
        if (string.IsNullOrEmpty(phone) || personalDataErased) return; // depersonalised: nobody to write to

        var now = clock.UtcNow;
        var outdatedAtUtc = now.AddMinutes(options.Value.GuestMessageTtlMinutes);
        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct);
        // ARCHITECTURE_CYCLE40.md §40.4/§40.5.5: the company's numbers are those of its billing account.
        var messaging = await messagingReader.ForCompanyAsync(company.Id, ct: ct);
        var optedOut = await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == phone, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — dispatch-time check against the message's own recipient phone (the booking's), not an account reading another subject's data

        var priorityTransport = settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;

        // Consent (a house booking or order always carries a bool mark; the planner calls only for a true one) → the cycle-40 gate → routing over the routable transports.
        var consent = await consentResolver.DecideAsync(notifyByMessenger, recipientUserId, phone, ct);
        var decision = MessagingQueueing.Decide(type, messaging, settings, optedOut, consent, now, outdatedAtUtc);
        if (decision.IsSkipped)
        {
            await AddIfNew(subject, phone, recipientName, recipientUserId, company, marker, type, string.Empty, priorityTransport, decision.RepresentativeChannelId, outdatedAtUtc,
                NotificationStatus.Skipped, decision.SkipReason, ct);
            return;
        }

        var body = bodyOf(UnsubscribeUrl(phone));
        foreach (var target in decision.Targets)
            await AddIfNew(subject, phone, recipientName, recipientUserId, company, marker, type, body, target.Transport, target.ChannelId, outdatedAtUtc, NotificationStatus.Pending, null, ct);
    }

    private string? UnsubscribeUrl(string phone)
    {
        var key = notificationOptions.Value.UnsubscribeKey;
        return string.IsNullOrEmpty(key) ? null : links.UnsubscribeUrl(UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(key)));
    }

    private async Task AddIfNew(
        StayNotificationSubject subject, string phone, string? recipientName, string? recipientUserId, Company company, string marker, NotificationType type, string body,
        NotificationTransport transport, Guid? channelId, DateTime outdatedAtUtc, NotificationStatus status, NotificationReason? reason, CancellationToken ct)
    {
        // One event/marker — one message per channel: the key is the whole guarantee (unique index), the existence check only avoids an exception.
        var key = $"{type}:{subject.KeyPart}:{marker}:{transport}";
        if (await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == key, ct)) return;
        db.OutboundNotifications.Add(new OutboundNotification
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, ChannelId = channelId, Transport = transport, StayBookingId = subject.StayBookingId,
            StayServiceOrderId = subject.StayServiceOrderId, Type = type,
            RecipientPhone = phone, RecipientName = recipientName, RecipientUserId = recipientUserId, Body = body,
            DueAtUtc = clock.UtcNow, VisitStartUtc = outdatedAtUtc, Status = status, Reason = reason, Generation = 0, IdempotencyKey = key,
        });
    }
}
