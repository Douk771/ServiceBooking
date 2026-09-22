using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE6.md §43.3.6 — a paid plan without an end date is not a valid state.
/// This guard was dropped when the cycle 7 rework replaced `AdminController.UpdateSubscription`
/// with `AdminBillingController.AssignSubscription`; regression coverage for the merge-review
/// finding that restored it.</summary>
public class SubscriptionAssignmentValidatorTests
{
    [Fact]
    public void PaidPlanWithoutPaidUntil_RequiresPaidUntil() =>
        SubscriptionAssignmentValidator.RequiresPaidUntil(Guid.NewGuid(), null).Should().BeTrue();

    [Fact]
    public void PaidPlanWithPaidUntil_DoesNotRequirePaidUntil() =>
        SubscriptionAssignmentValidator.RequiresPaidUntil(Guid.NewGuid(), new DateOnly(2026, 12, 31)).Should().BeFalse();

    [Fact]
    public void RemovingPlan_NeverRequiresPaidUntil_EvenWithoutADate() =>
        SubscriptionAssignmentValidator.RequiresPaidUntil(null, null).Should().BeFalse();

    [Fact]
    public void RemovingPlan_WithADateAnyway_StillDoesNotRequireIt() =>
        SubscriptionAssignmentValidator.RequiresPaidUntil(null, new DateOnly(2026, 12, 31)).Should().BeFalse();

    [Fact]
    public void ErrorText_MatchesTheCycle6Contract() =>
        SubscriptionAssignmentValidator.MissingPaidUntilError.Should().Be("Укажите дату окончания подписки");
}
