using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>BE-42-2 — the «Бани» tariff line as a slot vertical: plan resolution, warning texts, the channel option and the trial terms (ARCHITECTURE_CYCLE42.md §42.5).</summary>
public class BathsPlanTests
{
    private static readonly DateTime Now = new(2027, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    private static BathsSubscription Sub(Guid planId, int? maxResources, DateTime? until, bool active = true, bool planActive = true, CompanyKind line = CompanyKind.Baths) => new()
    {
        PlanConfigId = planId, IsActive = active, PaidUntil = until,
        PlanConfig = new SubscriptionPlanConfig { Id = planId, Name = "Тариф", MaxResources = maxResources, MaxHouses = 99, IsActive = planActive, Line = line, AllowNotificationChannel = true }
    };

    [Fact]
    public void No_subscription_means_no_plan() =>
        StaysPlanResolver.ResolveBaths(null, Now).Should().Be(StaysPlan.None);

    [Theory]
    [InlineData(true, true, 5, CompanyKind.Baths, true)]
    [InlineData(false, true, 5, CompanyKind.Baths, false)]
    [InlineData(true, false, 5, CompanyKind.Baths, false)]
    [InlineData(true, true, -1, CompanyKind.Baths, false)]
    [InlineData(true, true, 5, CompanyKind.Stays, false)]
    public void A_baths_plan_is_live_only_when_active_in_date_and_of_the_baths_line(bool active, bool planActive, int daysLeft, CompanyKind line, bool expected) =>
        StaysPlanResolver.ResolveBaths(Sub(BathsPlans.OneBathSeedId, 1, Now.AddDays(daysLeft), active, planActive, line), Now).HasActivePlan.Should().Be(expected);

    [Fact]
    public void The_unit_limit_of_baths_is_resources_never_houses()
    {
        var plan = StaysPlanResolver.ResolveBaths(Sub(BathsPlans.UpToThreeSeedId, 3, Now.AddDays(5)), Now);
        plan.MaxResources.Should().Be(3);
        plan.MaxHouses.Should().BeNull();
        plan.MaxUnits.Should().Be(3);
    }

    [Fact]
    public void The_trial_is_recognised_by_the_vertical_own_trial_plan_id()
    {
        StaysPlanResolver.ResolveBaths(Sub(BathsPlans.TrialSeedId, null, Now.AddDays(5)), Now).IsTrial.Should().BeTrue();
        StaysPlanResolver.ResolveBaths(Sub(BathsPlans.OneBathSeedId, 1, Now.AddDays(5)), Now).IsTrial.Should().BeFalse();
    }

    [Fact]
    public void Warning_levels_use_the_resource_unit()
    {
        var trial = Sub(BathsPlans.TrialSeedId, null, Now.AddDays(2.5));
        StaysPlanResolver.Warning(StaysPlanResolver.ResolveBaths(trial, Now), 0, Now, GateUnit.Resource).Level.Should().Be("TrialEnding3d");
        trial.PaidUntil = Now.AddDays(-1);
        StaysPlanResolver.Warning(StaysPlanResolver.ResolveBaths(trial, Now), 0, Now, GateUnit.Resource).Level.Should().Be("Expired");
        var paid = Sub(BathsPlans.OneBathSeedId, 1, Now.AddDays(10));
        var over = StaysPlanResolver.Warning(StaysPlanResolver.ResolveBaths(paid, Now), 3, Now, GateUnit.Resource);
        over.Level.Should().Be("OverLimit");
        over.Text.Should().Be("Опубликовано 3 ресурса при лимите 1: гости не могут бронировать. Снимите лишние ресурсы с публикации или смените тариф");
    }

    [Fact]
    public void The_house_warning_text_is_unchanged_by_the_unit_overload()
    {
        var paid = new StaysSubscription
        {
            PlanConfigId = StaysPlans.OneHouseSeedId, IsActive = true, PaidUntil = Now.AddDays(10),
            PlanConfig = new SubscriptionPlanConfig { Id = StaysPlans.OneHouseSeedId, Name = "Т", MaxHouses = 1, IsActive = true, Line = CompanyKind.Stays }
        };
        var plan = StaysPlanResolver.Resolve(paid, Now);
        StaysPlanResolver.Warning(plan, 3, Now, GateUnit.House).Should().Be(StaysPlanResolver.Warning(plan, 3, Now));
    }

    [Fact]
    public void A_number_is_paid_through_the_baths_line_only_when_the_account_has_baths_and_its_tariff_allows()
    {
        LineChannelGrant Baths(bool has, bool usable, OptionAvailability? rule) => new(CompanyKind.Baths, has, usable, rule);
        int Paid(params LineChannelGrant[] lines) => SubscriptionResolver.PaidNumbers(1, null, false, null, Now, lines);
        Paid(Baths(true, true, OptionAvailability.Included)).Should().Be(1);
        Paid(Baths(true, true, OptionAvailability.Extra)).Should().Be(1);
        Paid(Baths(false, true, OptionAvailability.Included)).Should().Be(0);
        Paid(Baths(true, false, OptionAvailability.Included)).Should().Be(0);
        Paid(Baths(true, true, OptionAvailability.Unavailable)).Should().Be(0);
        Paid(Baths(true, true, null)).Should().Be(0);
        Paid().Should().Be(0);
    }

    [Fact]
    public void The_option_row_own_period_still_applies_to_the_baths_line()
    {
        var lines = new[] { new LineChannelGrant(CompanyKind.Baths, true, true, OptionAvailability.Included) };
        SubscriptionResolver.PaidNumbers(2, Now.AddDays(-1), false, null, Now, lines).Should().Be(0);
        SubscriptionResolver.PaidNumbers(2, Now.AddDays(1), false, null, Now, lines).Should().Be(2);
    }

    [Fact]
    public void The_legacy_overload_equals_the_list_form_for_every_combination()
    {
        bool[] flags = [false, true];
        OptionAvailability?[] rules = [null, OptionAvailability.Unavailable, OptionAvailability.Extra];
        foreach (var servicesUsable in flags) foreach (var hasShops in flags) foreach (var ordersUsable in flags)
        foreach (var hasStays in flags) foreach (var staysUsable in flags) foreach (var rule in rules)
        {
            var legacy = SubscriptionResolver.PaidNumbers(3, null, servicesUsable, rule, hasShops, ordersUsable, rule, Now, hasStays, staysUsable, rule);
            var list = SubscriptionResolver.PaidNumbers(3, null, servicesUsable, rule, Now,
                [new LineChannelGrant(CompanyKind.Orders, hasShops, ordersUsable, rule), new LineChannelGrant(CompanyKind.Stays, hasStays, staysUsable, rule)]);
            legacy.Should().Be(list);
        }
    }

    [Fact]
    public void Each_vertical_owns_its_trial_plan_and_terms()
    {
        SlotVerticals.Stays.TrialPlanId.Should().Be(StaysPlans.TrialSeedId);
        SlotVerticals.Baths.TrialPlanId.Should().Be(BathsPlans.TrialSeedId);
        SlotVerticals.Stays.TrialTerms.Version.Should().Be(StaysTrialTerms.Version);
        SlotVerticals.Baths.TrialTerms.Version.Should().Be("baths-2026-10-09");
        SlotVerticals.Baths.TrialTerms.Sha256.Should().NotBe(SlotVerticals.Stays.TrialTerms.Sha256);
    }

    [Fact]
    public void Baths_trial_terms_name_the_line_the_days_and_the_messenger_channel()
    {
        var text = BathsTrialTerms.Text(14);
        text.Should().Contain("линейки «Бани» — 14 дней");
        text.Should().Contain("Сообщения гостям через подключённый канал WhatsApp или MAX доступны весь пробный период.");
        text.Should().NotContain("«Дома»").And.NotContain("ваши дома");
        BathsTrialTerms.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
