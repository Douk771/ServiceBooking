using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §573.3 — what to do when the trial plan has no <c>Included</c> rule for the mailing option
/// (<c>notifications.whatsapp</c>) at the moment a trial is granted.
///
/// Before cycle 28 that state was always a misconfiguration: the activation terms promised mailings, so it was logged as an
/// error with a GlitchTip signal on every grant. From cycle 28 the trial deliberately has no mailings (customer decision
/// Q28-4: the option is <c>Unavailable</c> on the trial plan and is offered to nobody). That is only a misconfiguration if
/// the plan itself was designed with mailings, i.e. <see cref="SubscriptionPlanConfig.AllowNotificationChannel"/> is on.
/// </summary>
public static class TrialMailingRulePolicy
{
    /// <summary>True when the missing/non-Included rule is an operator-visible misconfiguration (error log + signal);
    /// false when the trial plan is intentionally without mailings (information-level log only).</summary>
    public static bool IsMisconfiguration(SubscriptionPlanConfig trialPlan) => trialPlan.AllowNotificationChannel;
}
