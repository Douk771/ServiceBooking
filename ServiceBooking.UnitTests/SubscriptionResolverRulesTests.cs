using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class SubscriptionResolverRulesTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SubscriptionPlanConfig FullPlan(bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Full",
        AllowOnlineBooking = true,
        AllowMailing = true,
        AllowAnalytics = true,
        AllowPublicListing = true,
        AllowOnlinePayment = true,
        MaxEmployees = 10,
        MaxCompanies = 5,
        IsActive = isActive,
        PhotoQuotaMb = 5120,
        PhotoRetention = PhotoRetention.TwelveMonths,
    };

    [Fact]
    public void Resolve_NoSubscription_ReturnsFree()
    {
        var plan = SubscriptionResolver.Resolve(null, 0, Now);

        plan.Should().Be(EffectivePlan.Free);
    }

    [Fact]
    public void Resolve_SubscriptionInactive_ReturnsFree()
    {
        var sub = new AccountSubscription { IsActive = false, PlanConfig = FullPlan() };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.Should().Be(EffectivePlan.Free);
    }

    [Fact]
    public void Resolve_PaidUntilInThePast_ReturnsFree()
    {
        var sub = new AccountSubscription
        {
            IsActive = true, PaidUntil = Now.AddDays(-1), PlanConfig = FullPlan()
        };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.Should().Be(EffectivePlan.Free);
    }

    [Fact]
    public void Resolve_PaidUntilNull_ButActive_ReturnsPlan()
    {
        // No PaidUntil means "not on a metered cycle" (e.g. an admin-granted plan), not "expired".
        var sub = new AccountSubscription { IsActive = true, PaidUntil = null, PlanConfig = FullPlan() };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.Should().NotBe(EffectivePlan.Free);
        plan.AllowOnlineBooking.Should().BeTrue();
    }

    [Fact]
    public void Resolve_PlanConfigNull_ReturnsFree()
    {
        var sub = new AccountSubscription { IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = null };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.Should().Be(EffectivePlan.Free);
    }

    [Fact]
    public void Resolve_PlanConfigDeactivated_ReturnsFree_EvenThoughSubscriptionItselfIsActive()
    {
        // US-08: an admin-deactivated plan (e.g. discontinued tariff) must not keep granting its
        // features forever to whoever was on it when it was retired.
        var sub = new AccountSubscription
        {
            IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = FullPlan(isActive: false)
        };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.Should().Be(EffectivePlan.Free);
    }

    [Fact]
    public void Resolve_ActivePaidPlan_ReturnsPlanConfigValues()
    {
        var config = FullPlan();
        var sub = new AccountSubscription { IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = config };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.Should().Be(EffectivePlan.FromConfig(config));
    }

    // ── US-24: photo quota / retention travel the same pipe as every other flag ────────────

    [Fact]
    public void Resolve_NoSubscription_PhotoBaselineIsHundredMegabytesSixMonths()
    {
        var plan = SubscriptionResolver.Resolve(null, 0, Now);

        plan.PhotoQuotaMb.Should().Be(100);
        plan.PhotoRetention.Should().Be(PhotoRetention.SixMonths);
    }

    [Fact]
    public void Resolve_PlanConfigDeactivated_FallsBackToPhotoBaseline_NotThePlansOwnValues()
    {
        var sub = new AccountSubscription
        {
            IsActive = true, PaidUntil = Now.AddDays(30),
            PlanConfig = FullPlan(isActive: false) // PhotoQuotaMb = 5120, PhotoRetention = TwelveMonths on the config itself
        };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.PhotoQuotaMb.Should().Be(100);
        plan.PhotoRetention.Should().Be(PhotoRetention.SixMonths);
    }

    [Fact]
    public void Resolve_ActivePlanWithUnlimitedPhotoQuota_PreservesNull()
    {
        var config = FullPlan();
        config.PhotoQuotaMb = null;
        var sub = new AccountSubscription { IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = config };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.PhotoQuotaMb.Should().BeNull();
        plan.PhotoRetention.Should().Be(PhotoRetention.TwelveMonths);
    }

    // ── ARCHITECTURE_CYCLE5.md §44.3 p.8 / §54.4: GrandfatheredEmployeeBonus adds seats forever,
    // regardless of subscription state, and never appears in money. ─────────────────────────────

    [Fact]
    public void Resolve_ZeroBonus_LeavesAccountMaxEmployeesUnchanged()
    {
        var config = FullPlan();
        var sub = new AccountSubscription { IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = config };

        var plan = SubscriptionResolver.Resolve(sub, 0, Now);

        plan.AccountMaxEmployees.Should().Be(config.MaxEmployees);
    }

    [Fact]
    public void Resolve_PositiveBonus_AddsToAccountMaxEmployees_OnAPaidPlan()
    {
        var config = FullPlan();
        var sub = new AccountSubscription { IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = config };

        var plan = SubscriptionResolver.Resolve(sub, 4, Now);

        plan.AccountMaxEmployees.Should().Be(config.MaxEmployees + 4);
    }

    [Fact]
    public void Resolve_PositiveBonus_AddsToFreeBaseline_WhenNoUsableSubscription()
    {
        // The bonus survives a downgrade/expiry to Free by design — it was granted because the account
        // already had that many employees at migration time, and losing it the moment the subscription
        // lapses would defeat the whole point of grandfathering.
        var plan = SubscriptionResolver.Resolve(null, 3, Now);

        plan.AccountMaxEmployees.Should().Be(EffectivePlan.Free.AccountMaxEmployees + 3);
    }

    [Fact]
    public void Resolve_PositiveBonus_LeavesUnlimitedAccountMaxEmployeesAsNull()
    {
        var config = FullPlan();
        config.MaxEmployees = null;
        var sub = new AccountSubscription { IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = config };

        var plan = SubscriptionResolver.Resolve(sub, 5, Now);

        plan.AccountMaxEmployees.Should().BeNull();
    }

    [Fact]
    public void Resolve_NegativeOrZeroBonus_DoesNotAlterBasePlan()
    {
        // Defensive: BillingAccount.GrandfatheredEmployeeBonus should never go negative in practice, but
        // Resolve must not corrupt the base plan (e.g. subtract seats) if it somehow did.
        var config = FullPlan();
        var sub = new AccountSubscription { IsActive = true, PaidUntil = Now.AddDays(30), PlanConfig = config };

        var plan = SubscriptionResolver.Resolve(sub, -1, Now);

        plan.Should().Be(EffectivePlan.FromConfig(config));
    }
}
