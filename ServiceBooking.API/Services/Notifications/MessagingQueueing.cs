using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>What the shared queueing order decided for one event (§40.5.5): either a refusal with its reason, or the targets —
/// one per routed transport, each the transport's first number. <see cref="RepresentativeChannelId"/> is the channel a
/// <c>Skipped</c> row points at (the first number of the account in transport order), or <see langword="null"/>.</summary>
public sealed record QueueingDecision(
    NotificationReason? SkipReason, IReadOnlyList<NotificationRouting.Target> Targets, Guid? RepresentativeChannelId)
{
    public bool IsSkipped => SkipReason is not null;
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.5 — the part of "may this event be queued, and to which numbers" that the three schedulers (salon,
/// shop, houses) share: the cycle-40 gate (<see cref="NotificationGate"/>) over the account's state, then routing over the
/// ROUTABLE transports (§40.5.2). Pure: the schedulers read the facts and write the rows.
/// </summary>
public static class MessagingQueueing
{
    public static QueueingDecision Decide(
        NotificationType type, AccountMessagingState messaging, CompanyNotificationSettings? settings, bool recipientOptedOut,
        MessengerConsentDecision consent, DateTime nowUtc, DateTime visitStartUtc)
    {
        var representative = messaging.RepresentativeChannelId;

        var gate = NotificationGate.Evaluate(
            type, new MessagingAvailability(messaging.PlatformEnabled, messaging.AnyPaid, messaging.AnyRoutable),
            settings, recipientOptedOut, consent, nowUtc, visitStartUtc);
        if (!gate.IsAllowed) return new QueueingDecision(ToReason(gate.Reason!.Value), [], representative);

        var defaults = new CompanyNotificationSettings();
        var transports = NotificationRouting.SelectRoutedTransports(
            settings?.DeliveryMode ?? defaults.DeliveryMode, settings?.PriorityTransport ?? defaults.PriorityTransport,
            messaging.Transports.Where(t => t.Routable).Select(t => t.Transport));
        var targets = transports.Select(t => new NotificationRouting.Target(messaging.For(t).Primary!.Id, t)).ToList();
        return targets.Count == 0
            ? new QueueingDecision(NotificationReason.NoUsableChannel, [], representative)
            : new QueueingDecision(null, targets, representative);
    }

    /// <summary>The gate's reasons share their names with <see cref="NotificationReason"/> members (§40.5.3).</summary>
    public static NotificationReason ToReason(MessagingBlockReason reason) => Enum.Parse<NotificationReason>(reason.ToString());
}

/// <summary>
/// §40.11.2 — gathers the two facts <see cref="MessengerConsentRule"/> needs and asks it. A mark that is already <c>true</c>/<c>false</c>
/// needs no lookup. For <c>null</c> the recipient is an account when the record belongs to one (<paramref name="userId"/>) or when an
/// account has the recipient's phone CONFIRMED; the grant is the account's current <c>PdnConsent/ProviderDelivery</c>.
/// </summary>
public sealed class MessengerConsentResolver(AppDbContext db, ConsentLedger consentLedger)
{
    public async Task<MessengerConsentDecision> DecideAsync(bool? notifyByMessenger, string? userId, string recipientPhone, CancellationToken ct)
    {
        if (notifyByMessenger is not null) return MessengerConsentRule.Evaluate(notifyByMessenger, false, false);

        var accountUserId = userId;
        if (accountUserId is null)
        {
            // SUBJECT-PHONE-GATE: not-account-scoped — queueing-time check against the message's own recipient phone (already resolved upstream); only a CONFIRMED phone counts as that account's (ARCHITECTURE_CYCLE40.md §40.11.2, ARCHITECTURE_CYCLE16.md §245.3)
            accountUserId = await db.Users.AsNoTracking()
                .Where(u => u.PhoneNumber == recipientPhone && u.PhoneNumberConfirmed).Select(u => u.Id).FirstOrDefaultAsync(ct);
        }
        if (accountUserId is null) return MessengerConsentRule.Evaluate(null, false, false);

        var granted = await consentLedger.CurrentAsync(
            ConsentSubject.ForUser(accountUserId), LegalDocumentType.PdnConsent.ToString(), ConsentPurpose.ProviderDelivery, ct) is not null;
        return MessengerConsentRule.Evaluate(null, true, granted);
    }
}
