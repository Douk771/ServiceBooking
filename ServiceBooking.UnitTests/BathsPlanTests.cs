using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
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

    // Since cycle 40 (ARCHITECTURE_CYCLE40.md §40.3.1, A1) a number is paid by the account's OWN option row, never by a line's tariff rule:
    // «Бани» is therefore covered by the same pure function as every line. These tests pin that for an account that owns a «Бани» company.
    private static readonly LegacySubscriptionFacts NoServicesSubscription = new(false, null);

    private static TransportPayment Pay(OptionRowFacts? row, DateTime? trialEnd = null) =>
        ChannelOptionFunding.Evaluate(row, trialEnd, NoServicesSubscription, Now);

    [Fact]
    public void A_baths_only_account_without_an_option_row_has_no_paid_number_whatever_its_baths_plan_says()
    {
        var plan = StaysPlanResolver.ResolveBaths(Sub(BathsPlans.TrialSeedId, null, Now.AddDays(5)), Now);
        plan.HasActivePlan.Should().BeTrue();
        Pay(null).Paid.Should().BeFalse();
    }

    [Fact]
    public void An_option_rows_own_term_governs_the_number_of_a_baths_account()
    {
        Pay(new OptionRowFacts(null, Now.AddDays(1), false, Now.AddDays(-30), Quantity: 2)).Paid.Should().BeTrue();
        Pay(new OptionRowFacts(null, Now.AddDays(-1), false, Now.AddDays(-30), Quantity: 2)).Paid.Should().BeFalse();
        Pay(new OptionRowFacts(Now.AddDays(-1), Now.AddDays(10), false, Now.AddDays(-30))).Paid.Should().BeFalse("EndsAtUtc in the past closes the row");
    }

    [Fact]
    public void A_trial_row_is_paid_until_the_trial_end_and_not_after_it()
    {
        var trialRow = new OptionRowFacts(null, null, true, Now.AddDays(-3));
        Pay(trialRow, Now.AddDays(11)).Should().Match<TransportPayment>(p => p.Paid && p.IsTrial);
        Pay(trialRow, Now.AddDays(-1)).Paid.Should().BeFalse();
        Pay(trialRow, null).Paid.Should().BeFalse("no trial, no payment");
    }

    [Fact]
    public void A_transport_without_a_row_is_not_routable_even_inside_a_baths_trial()
    {
        var closed = new TransportOptionState(false, false, null);
        var state = AccountMessagingReader.BuildTransport(NotificationTransport.Max, null, Now.AddDays(5), NoServicesSubscription, Now, [], closed, true);
        state.Paid.Should().BeFalse();
        state.Routable.Should().BeFalse();
    }

    [Fact]
    public void Each_vertical_owns_its_trial_plan_and_terms()
    {
        SlotVerticals.Stays.TrialPlanId.Should().Be(StaysPlans.TrialSeedId);
        SlotVerticals.Baths.TrialPlanId.Should().Be(BathsPlans.TrialSeedId);
        SlotVerticals.Stays.TrialTerms.Version.Should().Be(StaysTrialTerms.Version);
        SlotVerticals.Baths.TrialTerms.Version.Should().Be("baths-2026-10-10");
        SlotVerticals.Baths.TrialTerms.Sha256.Should().NotBe(SlotVerticals.Stays.TrialTerms.Sha256);
    }

    [Fact]
    public void Baths_trial_terms_name_the_line_the_days_and_exclude_the_messenger()
    {
        var text = BathsTrialTerms.Text(14);
        text.Should().Contain("линейки «Бани» — 14 дней");
        text.Should().Contain("в пробный период не включаются").And.NotContain("доступны весь пробный период");
        text.Should().NotContain("«Дома»").And.NotContain("ваши дома");
        BathsTrialTerms.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
