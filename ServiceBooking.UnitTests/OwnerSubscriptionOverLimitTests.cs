using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Code-review finding (cycle 18 recheck, API_CONTRACT_CYCLE18.md §365, Т4) — the
/// overLimitCompanies/overLimitEmployees/overLimitText trio must describe "this account fell back to
/// the base Free tariff and is over ITS limits", never "usage exceeds whatever the current effective
/// plan happens to allow". Both reviewer scenarios (a paid plan an admin shrank, and a Free account
/// with a grandfathered employee bonus) are locked down here so the numbers in one response can never
/// contradict each other again.
/// </summary>
public class OwnerSubscriptionOverLimitTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private static SubscriptionPlanConfig NewPlanConfig(bool isActive, bool isSystemFree, int? maxCompanies, int? maxEmployees) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test plan",
        IsActive = isActive,
        IsSystemFree = isSystemFree,
        MaxCompanies = maxCompanies,
        MaxEmployees = maxEmployees,
    };

    private static AccountSubscription NewSub(SubscriptionPlanConfig? planConfig, bool isActive = true, DateTime? paidUntil = null) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = "owner-1",
        BillingAccountId = Guid.NewGuid(),
        PlanConfigId = planConfig?.Id,
        PlanConfig = planConfig,
        IsActive = isActive,
        PaidUntil = paidUntil,
    };

    // --- IsOnFreePlan ---

    [Fact]
    public void IsOnFreePlan_NoSubscription_IsTrue() =>
        OwnerSubscriptionService.IsOnFreePlan(null, Now).Should().BeTrue();

    [Fact]
    public void IsOnFreePlan_ExpiredSubscription_IsTrue() =>
        OwnerSubscriptionService.IsOnFreePlan(
            NewSub(NewPlanConfig(true, false, 5, 10), paidUntil: Now.AddDays(-1)), Now).Should().BeTrue();

    [Fact]
    public void IsOnFreePlan_ActivePaidPlanConfig_IsFalse() =>
        // Reviewer scenario 1: a paid, active plan config the admin later shrank to MaxCompanies=2 —
        // the account is still on a paid plan, not "fell back to Free".
        OwnerSubscriptionService.IsOnFreePlan(NewSub(NewPlanConfig(true, false, 2, 10)), Now).Should().BeFalse();

    [Fact]
    public void IsOnFreePlan_DirectlySubscribedToSystemFreeConfig_IsTrue() =>
        OwnerSubscriptionService.IsOnFreePlan(NewSub(NewPlanConfig(true, true, 1, 1)), Now).Should().BeTrue();

    [Fact]
    public void IsOnFreePlan_DeactivatedPlanConfig_FallsBackToFree_IsTrue() =>
        OwnerSubscriptionService.IsOnFreePlan(NewSub(NewPlanConfig(false, false, 5, 10)), Now).Should().BeTrue();

    // --- BuildOverLimit ---

    [Fact]
    public void BuildOverLimit_NotOnFreePlan_IsAlwaysZeroAndNull()
    {
        // Reviewer scenario 1: paid plan shrunk to MaxCompanies=2, 5 companies in use. Even though
        // usage is over THIS plan's own limit, the account isn't on Free, so the "fell back to Free"
        // fields must report nothing rather than quote the wrong tariff's limits.
        var plan = EffectivePlan.Free with { AccountMaxCompanies = 2, AccountMaxEmployees = 10 };
        var usage = new AccountUsage(Guid.NewGuid(), CompaniesUsed: 5, SeatsUsed: 3);

        var (overCompanies, overEmployees, text) = OwnerSubscriptionService.BuildOverLimit(plan, isOnFreePlan: false, usage);

        overCompanies.Should().Be(0);
        overEmployees.Should().Be(0);
        text.Should().BeNull();
    }

    [Fact]
    public void BuildOverLimit_OnFreePlan_WithGrandfatheredBonusFoldedIn_QuotesTheBoostedLimit()
    {
        // Reviewer scenario 2: Free account with GrandfatheredEmployeeBonus=3 → SubscriptionResolver
        // folds it into AccountMaxEmployees (1 + 3 = 4) before this method ever sees the plan; 5
        // employees in use is over THAT limit, not over the raw system Free limit of 1.
        var plan = EffectivePlan.Free with { AccountMaxEmployees = 4 };
        var usage = new AccountUsage(Guid.NewGuid(), CompaniesUsed: 1, SeatsUsed: 5);

        var (overCompanies, overEmployees, text) = OwnerSubscriptionService.BuildOverLimit(plan, isOnFreePlan: true, usage);

        overCompanies.Should().Be(0);
        overEmployees.Should().Be(1);
        text.Should().NotBeNull();
        text.Should().Contain("сотрудников: 4");
        text.Should().NotContain("сотрудников: 1,");
    }

    [Fact]
    public void BuildOverLimit_OnFreePlan_WithinLimits_ReturnsZeroAndNullText()
    {
        var plan = EffectivePlan.Free;
        var usage = new AccountUsage(Guid.NewGuid(), CompaniesUsed: 1, SeatsUsed: 1);

        var (overCompanies, overEmployees, text) = OwnerSubscriptionService.BuildOverLimit(plan, isOnFreePlan: true, usage);

        overCompanies.Should().Be(0);
        overEmployees.Should().Be(0);
        text.Should().BeNull();
    }

    [Fact]
    public void BuildOverLimit_OnFreePlan_UnboundedLimit_NeverFormatsTextEvenIfOtherLimitIsOver()
    {
        // Defensive regression for the null-limit/string.Format hazard called out in Т4 (§365): if
        // either limit somehow resolves to "unlimited" while the other is over, the legally-loaded
        // text must not render at all rather than print an empty slot.
        var plan = EffectivePlan.Free with { AccountMaxCompanies = null, AccountMaxEmployees = 1 };
        var usage = new AccountUsage(Guid.NewGuid(), CompaniesUsed: 100, SeatsUsed: 5);

        var (overCompanies, overEmployees, text) = OwnerSubscriptionService.BuildOverLimit(plan, isOnFreePlan: true, usage);

        overCompanies.Should().Be(0);
        overEmployees.Should().Be(4);
        text.Should().BeNull();
    }
}
