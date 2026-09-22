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
        // Cycle 7 (ARCHITECTURE_CYCLE7.md §45.3 p.3, §47.2): made an explicit, mandatory parameter
        // instead of an internal ChannelPaymentState.Of(channel, now) call — every caller must now say
        // out loud where it got "is this number funded" from, rather than the gate quietly re-deriving
        // it. Callers compute this via Services.Billing.ChannelFunding.Rank over the account's live
        // channels and plan.PaidNotificationNumbers (§47.1's N-vs-M rule) — no channel-level payment
        // read is left in this gate.
        bool channelIsFunded,
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

        // §47.2: "not on a paid plan" now means "the account has 0 paid notification numbers" — the
        // separate AllowNotificationChannel flag only gates whether the PLAN may buy the option at all,
        // which is a distinct question from whether it currently has any bought (see EffectivePlan's
        // own doc comment on the two fields).
        if (plan.PaidNotificationNumbers == 0)
            return NotificationGateResult.Block(NotificationReason.NotOnPaidPlan);

        if (!companyHasAssignment || channel is null)
            return NotificationGateResult.Block(NotificationReason.NoUsableChannel);

        // §47.2: an Unfunded channel (M > N, this one lost the ranking) blocks with the same reason as
        // "account not paid at all" — NotificationReason is append-only (§59) and NotOnPaidPlan already
        // honestly covers both "never paid" and "paid for fewer numbers than are configured".
        if (!channelIsFunded)
            return NotificationGateResult.Block(NotificationReason.NotOnPaidPlan);

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
