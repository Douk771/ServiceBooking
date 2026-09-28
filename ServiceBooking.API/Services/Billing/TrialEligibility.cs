namespace ServiceBooking.API.Services.Billing;

/// <summary>What <see cref="TrialEligibility.Evaluate"/> needs to know, already read from the database
/// by the caller (the function itself touches nothing).</summary>
/// <param name="Offered">The system trial plan exists, is active and public, and the platform's
/// duration/mailing-window settings are valid (§335.2 §1).</param>
/// <param name="SubscriptionUsable">The account's subscription is in force now — by the CALLER's own
/// rule: the activation service uses <see cref="SubscriptionUsability"/>, the state reader its trial-end
/// aware variant (cycle 18 "Б..." review fix); unchanged by the cycle 22 extraction.</param>
/// <param name="OnTrialPlan">That subscription is on the system trial plan.</param>
/// <param name="OnPaidPlan">That subscription's plan has a positive price (Д9).</param>
/// <param name="SubscriptionPaidUntil">Quoted in the AlreadyOnPaidPlan text.</param>
/// <param name="AlreadyUsed">The account's own past trial (§4).</param>
/// <param name="TrialStartedAtUtc">Quoted in the TrialAlreadyUsed text.</param>
/// <param name="HasVerifiedPhone">The owner has a confirmed phone (§5/§6).</param>
/// <param name="PhoneVerificationEnabled">The MAX-bot verification subsystem is switched on (R7).</param>
/// <param name="CanBypass">A superadmin emergency regrant (Mode.SuperAdminOverride with a reason).</param>
public sealed record TrialEligibilityFacts(
    bool Offered,
    bool SubscriptionUsable,
    bool OnTrialPlan,
    bool OnPaidPlan,
    DateTime? SubscriptionPaidUntil,
    bool AlreadyUsed,
    DateTime? TrialStartedAtUtc,
    bool HasVerifiedPhone,
    bool PhoneVerificationEnabled,
    bool CanBypass);

/// <summary>A refusal: the machine code and the owner-facing text (TrialLegalNotices).</summary>
public sealed record TrialRefusal(string Code, string Message);

/// <summary>
/// Cycle 22 D11, closes C18-11 — the ONE ordered set of trial-eligibility conditions shared by
/// <see cref="TrialActivationService.GrantAsync"/> (the real grant) and <see cref="TrialStateReader"/>
/// (its dry run for GET /api/billing/trial). Before this, the phone gate ("verified phone, else ask the
/// verification subsystem's state") and the paid-plan check lived as two hand-kept copies that could
/// drift apart; now both call this pure function, so the reason shown on the card and the reason a real
/// POST would give come from the same code.
///
/// Order is part of the contract (ARCHITECTURE_CYCLE18.md §335.2): platform state, then the account's
/// subscription, then its own past, then the phone. Checks only the activation service performs
/// (terms version, regrant reason, the uniqueness key — which need its transaction/configuration) stay
/// in the service, before and after this call, exactly where they were.
/// </summary>
public static class TrialEligibility
{
    public static TrialRefusal? Evaluate(TrialEligibilityFacts f)
    {
        // §1 — TrialNotOffered.
        if (!f.Offered)
            return new("TrialNotOffered", TrialLegalNotices.TrialRefusedPlanUnavailable);

        // §2 — TrialAlreadyActive: a usable subscription already on the trial plan.
        if (f.SubscriptionUsable && f.OnTrialPlan)
            return new("TrialAlreadyActive", TrialLegalNotices.TrialAlreadyActiveNotice);

        // §3 — AlreadyOnPaidPlan (Д9): a usable subscription on ANY plan with a positive price.
        if (f.SubscriptionUsable && f.OnPaidPlan)
            return new("AlreadyOnPaidPlan",
                string.Format(TrialLegalNotices.TrialRefusedActivePaidSubscription, f.SubscriptionPaidUntil?.ToString("dd.MM.yyyy")));

        // §4 — TrialAlreadyUsed: this account's own past. Bypassable only by an explicit override.
        if (f.AlreadyUsed && !f.CanBypass)
            return new("TrialAlreadyUsed",
                string.Format(TrialLegalNotices.TrialRefusedAlreadyUsedByAccount, f.TrialStartedAtUtc?.ToString("dd.MM.yyyy") ?? "ранее"));

        // §5/§6 + R7 — the owner's phone must be verified, unless this is an emergency regrant. A
        // verified phone satisfies the gate regardless of the subsystem's state (it exists to CONFIRM a
        // number, not to re-attest one already confirmed); only without one is the subsystem asked:
        // switched off → the honest "cannot verify right now", on → the ordinary prompt.
        if (!f.CanBypass && !f.HasVerifiedPhone)
            return f.PhoneVerificationEnabled
                ? new("PhoneNotVerified", TrialLegalNotices.TrialRefusedPhoneNotVerified)
                : new("PhoneVerificationUnavailable", TrialLegalNotices.TrialRefusedPhoneVerificationUnavailable);

        return null;
    }
}
