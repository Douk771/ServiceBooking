using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>
/// OwnerSubscriptionService.SubscriptionStatusFor's full truth table — locked down before N19's move
/// of the equivalent logic into SQL (AdminBillingController.GetBillingAccounts, cycle-07 backend
/// report item 5). The inline CASE expression in that controller must keep matching every branch
/// exercised here; it can't call this method directly (EF Core can't translate an arbitrary method
/// call into SQL), so this is the single source of truth both are checked against by hand.
/// </summary>
public class OwnerSubscriptionStatusTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static AccountSubscription NewSub(bool isActive, Guid? planConfigId, DateTime? paidUntil) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = "owner-1",
        BillingAccountId = Guid.NewGuid(),
        PlanConfigId = planConfigId,
        IsActive = isActive,
        PaidUntil = paidUntil,
    };

    [Fact]
    public void NullSubscription_IsFree() =>
        OwnerSubscriptionService.SubscriptionStatusFor(null, Now).Should().Be("Free");

    [Fact]
    public void SubscriptionWithoutAPlan_IsFree() =>
        OwnerSubscriptionService.SubscriptionStatusFor(NewSub(isActive: true, planConfigId: null, paidUntil: null), Now).Should().Be("Free");

    [Fact]
    public void InactiveSubscription_IsExpired_EvenWithoutAPaidUntilDate() =>
        OwnerSubscriptionService.SubscriptionStatusFor(NewSub(isActive: false, planConfigId: Guid.NewGuid(), paidUntil: null), Now).Should().Be("Expired");

    [Fact]
    public void ActiveSubscription_PastPaidUntil_IsExpired() =>
        OwnerSubscriptionService.SubscriptionStatusFor(
            NewSub(isActive: true, planConfigId: Guid.NewGuid(), paidUntil: Now.AddDays(-1)), Now).Should().Be("Expired");

    [Fact]
    public void ActiveSubscription_PaidUntilExactlyNow_IsStillActive_NotExpired()
    {
        // sub.PaidUntil < now — "expires at" is exclusive, matching the < comparison in
        // SubscriptionStatusFor (not <=), so this deliberately checks the boundary is NOT off-by-one.
        var status = OwnerSubscriptionService.SubscriptionStatusFor(
            NewSub(isActive: true, planConfigId: Guid.NewGuid(), paidUntil: Now), Now);
        status.Should().Be("Active");
    }

    [Fact]
    public void ActiveSubscription_FuturePaidUntil_IsActive() =>
        OwnerSubscriptionService.SubscriptionStatusFor(
            NewSub(isActive: true, planConfigId: Guid.NewGuid(), paidUntil: Now.AddDays(30)), Now).Should().Be("Active");

    [Fact]
    public void ActiveSubscription_NoPaidUntilDate_IsActive() =>
        OwnerSubscriptionService.SubscriptionStatusFor(
            NewSub(isActive: true, planConfigId: Guid.NewGuid(), paidUntil: null), Now).Should().Be("Active");

    [Fact]
    public void InactiveSubscription_WithAFuturePaidUntilDate_IsStillExpired() =>
        // IsActive == false wins over a still-in-the-future PaidUntil — an admin who deactivates a
        // subscription outright must see it as Expired immediately, not wait for PaidUntil to lapse.
        OwnerSubscriptionService.SubscriptionStatusFor(
            NewSub(isActive: false, planConfigId: Guid.NewGuid(), paidUntil: Now.AddDays(30)), Now).Should().Be("Expired");
}
