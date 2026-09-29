using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Cycle 22 D11 (closes C18-11) — the one ordered set of trial-eligibility conditions shared by
/// TrialActivationService.GrantAsync and TrialStateReader. Codes and texts must be exactly the ones both
/// call sites produced before the extraction (ARCHITECTURE_CYCLE18.md §335.2).
/// </summary>
public class TrialEligibilityTests
{
    private static readonly DateTime PaidUntil = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Started = new(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>An owner who passes every check.</summary>
    private static TrialEligibilityFacts Eligible() => new(
        Offered: true, SubscriptionUsable: false, OnTrialPlan: false, OnPaidPlan: false, SubscriptionPaidUntil: null,
        AlreadyUsed: false, TrialStartedAtUtc: null, HasVerifiedPhone: true, PhoneVerificationEnabled: true, CanBypass: false);

    [Fact]
    public void Eligible_NoRefusal() => TrialEligibility.Evaluate(Eligible()).Should().BeNull();

    [Fact]
    public void NotOffered_WinsOverEverythingElse() =>
        TrialEligibility.Evaluate(Eligible() with
        {
            Offered = false, SubscriptionUsable = true, OnTrialPlan = true, OnPaidPlan = true, AlreadyUsed = true,
            HasVerifiedPhone = false, CanBypass = true,
        }).Should().Be(new TrialRefusal("TrialNotOffered", TrialLegalNotices.TrialRefusedPlanUnavailable));

    [Fact]
    public void UsableSubscriptionOnTrialPlan_IsAlreadyActive_BeforePaidPlanCheck() =>
        TrialEligibility.Evaluate(Eligible() with { SubscriptionUsable = true, OnTrialPlan = true, OnPaidPlan = true })
            .Should().Be(new TrialRefusal("TrialAlreadyActive", TrialLegalNotices.TrialAlreadyActiveNotice));

    [Fact]
    public void UsablePaidSubscription_IsAlreadyOnPaidPlan_QuotingItsEndDate() =>
        TrialEligibility.Evaluate(Eligible() with { SubscriptionUsable = true, OnPaidPlan = true, SubscriptionPaidUntil = PaidUntil })
            .Should().Be(new TrialRefusal("AlreadyOnPaidPlan",
                string.Format(TrialLegalNotices.TrialRefusedActivePaidSubscription, "15.10.2026")));

    [Fact]
    public void PaidPlanNotInForce_DoesNotRefuse() =>
        TrialEligibility.Evaluate(Eligible() with { SubscriptionUsable = false, OnPaidPlan = true, OnTrialPlan = true })
            .Should().BeNull();

    [Fact]
    public void AlreadyUsed_QuotesStartDate_OrRaneeWithoutOne()
    {
        TrialEligibility.Evaluate(Eligible() with { AlreadyUsed = true, TrialStartedAtUtc = Started })
            .Should().Be(new TrialRefusal("TrialAlreadyUsed",
                string.Format(TrialLegalNotices.TrialRefusedAlreadyUsedByAccount, "02.03.2026")));
        TrialEligibility.Evaluate(Eligible() with { AlreadyUsed = true })
            .Should().Be(new TrialRefusal("TrialAlreadyUsed",
                string.Format(TrialLegalNotices.TrialRefusedAlreadyUsedByAccount, "ранее")));
    }

    [Fact]
    public void AlreadyUsed_ComesBeforeThePhoneGate() =>
        TrialEligibility.Evaluate(Eligible() with { AlreadyUsed = true, HasVerifiedPhone = false })!.Code
            .Should().Be("TrialAlreadyUsed");

    [Theory]
    [InlineData(true, "PhoneNotVerified")]
    [InlineData(false, "PhoneVerificationUnavailable")]
    public void NoVerifiedPhone_AsksTheSubsystemState(bool subsystemEnabled, string expectedCode)
    {
        var refusal = TrialEligibility.Evaluate(Eligible() with { HasVerifiedPhone = false, PhoneVerificationEnabled = subsystemEnabled });

        refusal!.Code.Should().Be(expectedCode);
        refusal.Message.Should().Be(subsystemEnabled
            ? TrialLegalNotices.TrialRefusedPhoneNotVerified
            : TrialLegalNotices.TrialRefusedPhoneVerificationUnavailable);
    }

    [Fact]
    public void VerifiedPhone_SatisfiesTheGate_EvenWithTheSubsystemOff() =>
        TrialEligibility.Evaluate(Eligible() with { HasVerifiedPhone = true, PhoneVerificationEnabled = false })
            .Should().BeNull();

    [Fact]
    public void SuperAdminOverride_BypassesOwnPastAndPhone_ButNotSubscriptionState()
    {
        TrialEligibility.Evaluate(Eligible() with
        {
            CanBypass = true, AlreadyUsed = true, HasVerifiedPhone = false, PhoneVerificationEnabled = false,
        }).Should().BeNull();

        TrialEligibility.Evaluate(Eligible() with { CanBypass = true, SubscriptionUsable = true, OnPaidPlan = true })!.Code
            .Should().Be("AlreadyOnPaidPlan");
        TrialEligibility.Evaluate(Eligible() with { CanBypass = true, SubscriptionUsable = true, OnTrialPlan = true })!.Code
            .Should().Be("TrialAlreadyActive");
    }
}
