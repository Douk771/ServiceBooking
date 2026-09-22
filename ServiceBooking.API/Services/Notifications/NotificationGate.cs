using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

public enum NotificationGateOutcome
{
    Allowed,
    Blocked,
}

public readonly record struct NotificationGateResult(NotificationGateOutcome Outcome, NotificationReason? Reason)
{
    public static readonly NotificationGateResult Allow = new(NotificationGateOutcome.Allowed, null);
    public static NotificationGateResult Block(NotificationReason reason) => new(NotificationGateOutcome.Blocked, reason);
}

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
    // providerDeliveryConsentMode: T-24 (ARCHITECTURE_CYCLE5.md §52.3) — a config parameter, not a
    // hardcoded branch; see ProviderDeliveryConsentMode for what each value means. Defaults to
    // AccountsOnly so every existing caller (and every existing test) that doesn't pass it explicitly
    // keeps today's behavior for guests (never blocked) and gains the new check only for recipients WITH
    // an account.
    // recipientHasProviderDeliveryConsent: null means "no account" (a guest — structurally cannot have
    // granted anything, §52.3's AccountsOnly row); true/false means the caller already looked up the
    // recipient's current PdnConsent/ProviderDelivery grant via ConsentLedger.
    public static NotificationGateResult Evaluate(
        EffectivePlan plan,
        NotificationType type,
        bool companyHasAssignment,
        NotificationChannel? channel,
        CompanyNotificationSettings? settings,
        bool recipientOptedOut,
        DateTime nowUtc,
        DateTime visitStartUtc,
        ProviderDeliveryConsentMode providerDeliveryConsentMode = ProviderDeliveryConsentMode.AccountsOnly,
        bool? recipientHasProviderDeliveryConsent = null)
    {
        if (recipientOptedOut)
            return NotificationGateResult.Block(NotificationReason.RecipientOptedOut);

        // T-24 (ARCHITECTURE_CYCLE5.md §52.3). Off: never checked here — reliance is on named disclosure
        // alone (§52.4 step 1 requires D4 to be rewritten before this value is ever used). AccountsOnly:
        // a guest (recipientHasProviderDeliveryConsent == null) is never blocked — nobody asked them,
        // the contractual basis for the booking itself covers delivery; an account holder who has NOT
        // granted (or has revoked) the purpose IS blocked. Strict: even a guest is blocked, since they
        // structurally cannot satisfy "has an explicit, current grant" — §52.4's "ужесточение", a
        // deliberate product decision the flag alone does not soften.
        var blockedByProviderDeliveryConsent = providerDeliveryConsentMode switch
        {
            ProviderDeliveryConsentMode.Off => false,
            ProviderDeliveryConsentMode.AccountsOnly => recipientHasProviderDeliveryConsent == false,
            ProviderDeliveryConsentMode.Strict => recipientHasProviderDeliveryConsent != true,
            _ => throw new ArgumentOutOfRangeException(nameof(providerDeliveryConsentMode))
        };
        if (blockedByProviderDeliveryConsent)
            return NotificationGateResult.Block(NotificationReason.NoProviderDeliveryConsent);

        if (!plan.AllowNotificationChannel)
            return NotificationGateResult.Block(NotificationReason.NotOnPaidPlan);

        if (!companyHasAssignment || channel is null)
            return NotificationGateResult.Block(NotificationReason.NoUsableChannel);

        if (ChannelPaymentState.Of(channel, nowUtc) != ChannelPaymentStatus.Paid)
            return NotificationGateResult.Block(NotificationReason.NoUsableChannel);

        var enabledTypeMask = settings?.EnabledTypeMask ?? CompanyNotificationSettings.DefaultEnabledTypeMask;
        if ((enabledTypeMask & (1 << (int)type)) == 0)
            return NotificationGateResult.Block(NotificationReason.TypeDisabledByCompany);

        // B3 / SPEC US-31 п. 2: the "less than N minutes before the visit" threshold is a safety valve
        // against dumping an accumulated reminder queue after a reconnect — it does NOT apply to
        // confirmation, cancellation or reschedule, which are reactions to the salon's own action right
        // now and must go out regardless of how close the visit is.
        if (type == NotificationType.Reminder)
        {
            var minLeadMinutes = settings?.MinLeadMinutes ?? new CompanyNotificationSettings().MinLeadMinutes;
            if (NotificationTiming.IsBelowMinimumLeadTime(visitStartUtc, nowUtc, minLeadMinutes))
                return NotificationGateResult.Block(NotificationReason.BelowMinimumLeadTime);
        }

        return NotificationGateResult.Allow;
    }
}
