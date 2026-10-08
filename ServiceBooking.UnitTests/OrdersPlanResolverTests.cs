using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §459.2 — OrdersPlanResolver.Resolve (pure) and the paid-numbers rule of §457.4.</summary>
public class OrdersPlanResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SubscriptionPlanConfig Plan(Action<SubscriptionPlanConfig>? tweak = null)
    {
        var p = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = "Заказы · Бизнес", Line = CompanyKind.Orders, IsActive = true, MaxCompanies = 3, MaxEmployees = 10,
            MaxProductsPerShop = 1000, MaxOrdersPerMonth = null, AllowOrders = true, AllowNotificationChannel = true
        };
        tweak?.Invoke(p);
        return p;
    }

    private static SubscriptionPlanConfig Free() => Plan(p => { p.Name = "Заказы · Бесплатно"; p.IsSystemFree = true; p.MaxCompanies = 1; p.MaxEmployees = 2; p.MaxProductsPerShop = 50; p.MaxOrdersPerMonth = 150; });

    private static OrdersSubscription Sub(SubscriptionPlanConfig plan, Action<OrdersSubscription>? tweak = null)
    {
        var s = new OrdersSubscription { PlanConfigId = plan.Id, PlanConfig = plan, IsActive = true };
        tweak?.Invoke(s);
        return s;
    }

    [Fact]
    public void NoSubscription_IsTheSystemFreeTier_WithItsNumbers()
    {
        var r = OrdersPlanResolver.Resolve(null, Free(), Now);
        (r.IsFreeTier, r.Usable, r.PlanName).Should().Be((true, true, "Заказы · Бесплатно"));
        (r.MaxShops, r.MaxSeats, r.MaxProductsPerShop, r.MaxOrdersPerMonth).Should().Be((1, 2, 50, 150));
    }

    [Fact]
    public void UsableSubscription_GivesItsOwnPlan()
    {
        var plan = Plan();
        var r = OrdersPlanResolver.Resolve(Sub(plan), Free(), Now);
        (r.IsFreeTier, r.PlanId, r.MaxShops, r.MaxSeats, r.MaxOrdersPerMonth).Should().Be((false, plan.Id, 3, 10, (int?)null));
    }

    [Fact]
    public void PaidUntil_IsInclusive_AndNullIsForever()
    {
        var plan = Plan();
        OrdersPlanResolver.Resolve(Sub(plan, s => s.PaidUntil = Now), Free(), Now).IsFreeTier.Should().BeFalse();
        OrdersPlanResolver.Resolve(Sub(plan, s => s.PaidUntil = Now.AddSeconds(-1)), Free(), Now).IsFreeTier.Should().BeTrue();
        OrdersPlanResolver.Resolve(Sub(plan, s => s.PaidUntil = null), Free(), Now).IsFreeTier.Should().BeFalse();
    }

    [Fact]
    public void InactiveSubscription_InactivePlan_OrPlanOfAnotherLine_FallBackToFree()
    {
        var plan = Plan();
        OrdersPlanResolver.Resolve(Sub(plan, s => s.IsActive = false), Free(), Now).IsFreeTier.Should().BeTrue();
        OrdersPlanResolver.Resolve(Sub(Plan(p => p.IsActive = false)), Free(), Now).IsFreeTier.Should().BeTrue();
        OrdersPlanResolver.Resolve(Sub(Plan(p => p.Line = CompanyKind.Services)), Free(), Now).IsFreeTier.Should().BeTrue();
    }

    [Fact]
    public void MissingSystemFreeTariff_UsesTheSeedNumbers()
    {
        var r = OrdersPlanResolver.Resolve(null, null, Now);
        r.Should().Be(OrdersPlan.FallbackFree);
        (r.MaxShops, r.MaxSeats, r.MaxProductsPerShop, r.MaxOrdersPerMonth, r.AllowOrders).Should().Be((1, 2, 50, 150, true));
        r.PlanId.Should().Be(OrdersFreePlan.SeedId);
    }

    [Fact]
    public void FreeTariffOfTheOtherLine_IsNotAcceptedAsTheFreeTierOfOrders()
    {
        var servicesFree = Plan(p => { p.Line = CompanyKind.Services; p.IsSystemFree = true; });
        OrdersPlanResolver.Resolve(null, servicesFree, Now).Should().Be(OrdersPlan.FallbackFree);
    }

    [Fact]
    public void PlanFlags_AreCarried()
    {
        var r = OrdersPlanResolver.Resolve(Sub(Plan(p => { p.AllowOrders = false; })), Free(), Now);
        r.AllowOrders.Should().BeFalse();
    }
}
