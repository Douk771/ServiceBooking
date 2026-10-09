using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// "Can this notification be queued/sent at all?" — one rule, used everywhere the question comes up
/// (ARCHITECTURE_CYCLE4.md §23.2, SPEC §4.0): the HTTP 402 gates, queueing (→ <c>Skipped</c>), and the
/// settings screen's <c>effectiveEnabled</c>/<c>blockedReason</c>. Pure: no DB, no HTTP.
///
/// Deliberately does NOT check whether the channel is currently <see cref="ChannelState.Connected"/> —
/// that isn't a "should this be skipped" question, it's "hold it for later" (§30.2: unconnected doesn't
/// discard queued rows, it just doesn't dispatch them yet), and belongs to the dispatcher's own
/// selection logic, not this gate.
/// </summary>
public static class NotificationGate
{
    /// <summary>
    /// The gate (ARCHITECTURE_CYCLE40.md §40.5.3): no plan, no assignment, no per-channel funding — those questions moved to
    /// <see cref="MessagingAvailability"/>. Order: platform switch → unsubscribe (checked HERE, not by
    /// <see cref="MessengerConsentRule"/>, and stronger than any opt-in mark) → client declined → no consent →
    /// nothing paid → nothing routable → type disabled by the company → lead time. Pure.
    /// </summary>
    public static MessagingGateResult Evaluate(
        NotificationType type,
        MessagingAvailability availability,
        CompanyNotificationSettings? settings,
        bool recipientOptedOut,
        MessengerConsentDecision consent,
        DateTime nowUtc,
        DateTime visitStartUtc)
    {
        if (!availability.PlatformEnabled) return MessagingGateResult.Block(MessagingBlockReason.PlatformMessagingDisabled);
        if (recipientOptedOut) return MessagingGateResult.Block(MessagingBlockReason.RecipientOptedOut);
        if (consent == MessengerConsentDecision.Declined) return MessagingGateResult.Block(MessagingBlockReason.ClientDeclinedMessenger);
        if (consent == MessengerConsentDecision.NoConsent) return MessagingGateResult.Block(MessagingBlockReason.NoProviderDeliveryConsent);
        if (!availability.AnyTransportPaid) return MessagingGateResult.Block(MessagingBlockReason.NotOnPaidPlan);
        if (!availability.AnyTransportRoutable) return MessagingGateResult.Block(MessagingBlockReason.NoUsableChannel);

        var enabledTypeMask = settings?.EnabledTypeMask ?? CompanyNotificationSettings.DefaultEnabledTypeMask;
        if (NotificationTypeCatalog.IsBookingType(type) && (enabledTypeMask & (1 << (int)type)) == 0)
            return MessagingGateResult.Block(MessagingBlockReason.TypeDisabledByCompany);

        if (type == NotificationType.Reminder)
        {
            var minLeadMinutes = settings?.MinLeadMinutes ?? new CompanyNotificationSettings().MinLeadMinutes;
            if (NotificationTiming.IsBelowMinimumLeadTime(visitStartUtc, nowUtc, minLeadMinutes))
                return MessagingGateResult.Block(MessagingBlockReason.BelowMinimumLeadTime);
        }

        return MessagingGateResult.Allow;
    }
}

/// <summary>Account-level facts of the cycle-40 gate (§40.5.3).</summary>
public readonly record struct MessagingAvailability(bool PlatformEnabled, bool AnyTransportPaid, bool AnyTransportRoutable);

/// <summary>Why the cycle-40 gate refuses. Member names equal the matching <see cref="NotificationReason"/> members
/// (the three new ones come with BE-40-M), so the caller maps by name.</summary>
public enum MessagingBlockReason
{
    PlatformMessagingDisabled,
    RecipientOptedOut,
    ClientDeclinedMessenger,
    NoProviderDeliveryConsent,
    NotOnPaidPlan,
    NoUsableChannel,
    TypeDisabledByCompany,
    BelowMinimumLeadTime,
}

public readonly record struct MessagingGateResult(MessagingBlockReason? Reason)
{
    public static readonly MessagingGateResult Allow = new(null);
    public static MessagingGateResult Block(MessagingBlockReason reason) => new(reason);
    public bool IsAllowed => Reason is null;
}
