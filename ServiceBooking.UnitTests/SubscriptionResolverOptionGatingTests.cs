using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// N12/N13/N14 — <see cref="SubscriptionResolver.IsOptionCurrentlyPaid"/> is the pure decision that
/// <c>GetEffectivePlansForAccountsAsync</c> uses to turn a purchased <c>AccountSubscriptionOption</c>
/// row into a counted quantity. Extracted specifically so these three previously-conflated gates can be
/// exercised independently, without a database.
/// </summary>
public class SubscriptionResolverOptionGatingTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ExpiredSubscription_OptionNotCounted_EvenWithOwnFuturePaidUntil()
    {
        // N12 — the exact regression: subscription unusable, but the option's own PaidUntilUtc is
        // still in the future. Must NOT be counted; an option cannot outlive the plan it rides on.
        var result = SubscriptionResolver.IsOptionCurrentlyPaid(
            subscriptionUsable: false, optionPaidUntilUtc: Now.AddDays(20), currentPlanAvailability: OptionAvailability.Extra, Now);

        result.Should().BeFalse();
    }

    [Fact]
    public void UsableSubscription_OwnPaidUntilInPast_OptionNotCounted()
    {
        var result = SubscriptionResolver.IsOptionCurrentlyPaid(
            subscriptionUsable: true, optionPaidUntilUtc: Now.AddDays(-1), currentPlanAvailability: OptionAvailability.Extra, Now);

        result.Should().BeFalse();
    }

    [Fact]
    public void UsableSubscription_NoOwnPaidUntil_RidesTheSubscriptionsOwnPeriod()
    {
        var result = SubscriptionResolver.IsOptionCurrentlyPaid(
            subscriptionUsable: true, optionPaidUntilUtc: null, currentPlanAvailability: OptionAvailability.Extra, Now);

        result.Should().BeTrue();
    }

    [Fact]
    public void UsableSubscription_OwnFuturePaidUntil_Counted()
    {
        var result = SubscriptionResolver.IsOptionCurrentlyPaid(
            subscriptionUsable: true, optionPaidUntilUtc: Now.AddDays(5), currentPlanAvailability: OptionAvailability.Extra, Now);

        result.Should().BeTrue();
    }

    [Fact]
    public void CurrentPlanRuleIsUnavailable_OptionNotCounted_EvenIfStillPaid()
    {
        // N13 — downgrading to a plan whose rule for this option is Unavailable must switch it off,
        // even though the AccountSubscriptionOption row itself is still fully paid up.
        var result = SubscriptionResolver.IsOptionCurrentlyPaid(
            subscriptionUsable: true, optionPaidUntilUtc: Now.AddDays(30), currentPlanAvailability: OptionAvailability.Unavailable, Now);

        result.Should().BeFalse();
    }

    [Fact]
    public void MissingRuleForCurrentPlan_TreatedAsUnavailable_FailClosed()
    {
        // N13/N14 — no PlanOptionRule row for (current plan, option) means Unavailable, not Extra;
        // fail closed on money, same convention as everywhere else OptionAvailability is read.
        var result = SubscriptionResolver.IsOptionCurrentlyPaid(
            subscriptionUsable: true, optionPaidUntilUtc: null, currentPlanAvailability: null, Now);

        result.Should().BeFalse();
    }

    [Fact]
    public void CurrentPlanRuleIsIncluded_OptionCounted()
    {
        var result = SubscriptionResolver.IsOptionCurrentlyPaid(
            subscriptionUsable: true, optionPaidUntilUtc: null, currentPlanAvailability: OptionAvailability.Included, Now);

        result.Should().BeTrue();
    }
}
