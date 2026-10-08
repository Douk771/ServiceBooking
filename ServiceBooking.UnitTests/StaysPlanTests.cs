using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class StaysPlanTests
{
    private static readonly DateTime Now = new(2027, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    private static StaysSubscription Sub(Guid planId, int? maxHouses, DateTime? until, bool active = true, bool planActive = true, CompanyKind line = CompanyKind.Stays) => new()
    {
        PlanConfigId = planId, IsActive = active, PaidUntil = until,
        PlanConfig = new SubscriptionPlanConfig { Id = planId, Name = "Тариф", MaxHouses = maxHouses, IsActive = planActive, Line = line, AllowNotificationChannel = true }
    };

    [Fact]
    public void No_subscription_means_no_plan_there_is_no_free_tier()
    {
        var plan = StaysPlanResolver.Resolve(null, Now);
        plan.HasActivePlan.Should().BeFalse();
        plan.WasEverSubscribed.Should().BeFalse();
        StaysPlanResolver.Warning(plan, 0, Now).Level.Should().Be("NoPlan");
    }

    [Theory]
    [InlineData(true, true, 5, true)]
    [InlineData(false, true, 5, false)]
    [InlineData(true, false, 5, false)]
    [InlineData(true, true, -1, false)]
    public void A_plan_is_live_only_when_active_in_date_and_the_tariff_is_active(bool active, bool planActive, int daysLeft, bool expected) =>
        StaysPlanResolver.Resolve(Sub(StaysPlans.OneHouseSeedId, 1, Now.AddDays(daysLeft), active, planActive), Now).HasActivePlan.Should().Be(expected);

    [Fact]
    public void A_tariff_of_another_line_is_never_live() =>
        StaysPlanResolver.Resolve(Sub(Guid.NewGuid(), 1, Now.AddDays(5), line: CompanyKind.Services), Now).HasActivePlan.Should().BeFalse();

    [Fact]
    public void Warning_levels()
    {
        var trial = Sub(StaysPlans.TrialSeedId, null, Now.AddDays(2.5));
        StaysPlanResolver.Warning(StaysPlanResolver.Resolve(trial, Now), 0, Now).Level.Should().Be("TrialEnding3d");
        trial.PaidUntil = Now.AddHours(20);
        StaysPlanResolver.Warning(StaysPlanResolver.Resolve(trial, Now), 0, Now).Level.Should().Be("TrialEnding1d");
        trial.PaidUntil = Now.AddDays(10);
        StaysPlanResolver.Warning(StaysPlanResolver.Resolve(trial, Now), 0, Now).Level.Should().Be("None");
        trial.PaidUntil = Now.AddDays(-1);
        StaysPlanResolver.Warning(StaysPlanResolver.Resolve(trial, Now), 0, Now).Level.Should().Be("Expired");
        var paid = Sub(StaysPlans.OneHouseSeedId, 1, Now.AddDays(10));
        var over = StaysPlanResolver.Warning(StaysPlanResolver.Resolve(paid, Now), 3, Now);
        over.Level.Should().Be("OverLimit");
        over.Text.Should().Contain("Опубликовано 3 дома при лимите 1");
    }

    [Fact]
    public void A_number_is_paid_through_the_stays_line_only_when_the_account_has_stays_and_its_tariff_allows()
    {
        var now = DateTime.UtcNow;
        // salons say no, shops absent, stays yes
        SubscriptionResolver.PaidNumbers(1, null, false, null, false, false, null, now, accountHasStays: true, staysUsable: true, staysRule: OptionAvailability.Included).Should().Be(1);
        SubscriptionResolver.PaidNumbers(1, null, false, null, false, false, null, now, accountHasStays: false, staysUsable: true, staysRule: OptionAvailability.Included).Should().Be(0);
        SubscriptionResolver.PaidNumbers(1, null, false, null, false, false, null, now, accountHasStays: true, staysUsable: false, staysRule: OptionAvailability.Included).Should().Be(0);
        SubscriptionResolver.PaidNumbers(1, null, false, null, false, false, null, now, accountHasStays: true, staysUsable: true, staysRule: OptionAvailability.Unavailable).Should().Be(0);
        // the cycle-24 behaviour is untouched
        SubscriptionResolver.PaidNumbers(2, null, true, OptionAvailability.Extra, false, false, null, now).Should().Be(2);
    }
}
