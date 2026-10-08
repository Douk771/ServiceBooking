using FluentAssertions;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class StaysHouseRulesTests
{
    private static HousePricePeriod P(string s, string e, int price = 1000) =>
        new() { Id = Guid.NewGuid(), StartDate = DateOnly.Parse(s), EndDate = DateOnly.Parse(e), PriceRub = price };

    [Fact]
    public void Long_periods_may_not_overlap_but_may_touch_days_apart()
    {
        var others = new[] { P("2027-01-10", "2027-01-20") };
        HousePricePeriodRules.FindConflict(others, DateOnly.Parse("2027-01-20"), DateOnly.Parse("2027-01-25")).Should().NotBeNull("end is inclusive");
        HousePricePeriodRules.FindConflict(others, DateOnly.Parse("2027-01-21"), DateOnly.Parse("2027-01-25")).Should().BeNull();
        HousePricePeriodRules.FindConflict(others, DateOnly.Parse("2027-01-01"), DateOnly.Parse("2027-01-09")).Should().BeNull();
    }

    [Fact]
    public void A_one_day_period_may_lie_inside_a_long_one_but_two_one_day_periods_may_not_share_a_date()
    {
        var long1 = P("2027-01-10", "2027-01-20");
        var one = P("2027-01-15", "2027-01-15", 9000);
        HousePricePeriodRules.FindConflict(new[] { long1 }, one.StartDate, one.EndDate).Should().BeNull();
        HousePricePeriodRules.FindConflict(new[] { long1, one }, DateOnly.Parse("2027-01-15"), DateOnly.Parse("2027-01-15")).Should().Be(one);
        // a long period covering an existing one-day period is fine as well
        HousePricePeriodRules.FindConflict(new[] { one }, DateOnly.Parse("2027-01-10"), DateOnly.Parse("2027-01-20")).Should().BeNull();
    }

    [Fact]
    public void Editing_a_period_does_not_conflict_with_itself()
    {
        var p = P("2027-01-10", "2027-01-20");
        HousePricePeriodRules.FindConflict(new[] { p }, p.StartDate, p.EndDate.AddDays(2), p.Id).Should().BeNull();
    }

    [Theory]
    [InlineData("2027-01-10", "2027-01-09", 1000, "Дата окончания не может быть раньше начала")]
    [InlineData("2027-01-10", "2029-02-01", 1000, "Период — не длиннее двух лет")]
    [InlineData("2027-01-10", "2027-01-12", 0, "Цена — от 1 до 1 000 000 ₽")]
    [InlineData("2027-01-10", "2027-01-12", 1000001, "Цена — от 1 до 1 000 000 ₽")]
    public void Validation_texts(string s, string e, int price, string expected) =>
        HousePricePeriodRules.Validate(DateOnly.Parse(s), DateOnly.Parse(e), price).Should().Be(expected);

    [Fact]
    public void Two_year_period_is_the_longest_allowed() =>
        HousePricePeriodRules.Validate(DateOnly.Parse("2027-01-01"), DateOnly.Parse("2027-01-01").AddDays(730), 1000).Should().BeNull();

    [Fact]
    public void Amenities_mask_round_trips_and_labels_exist()
    {
        var all = Enum.GetValues<HouseAmenity>();
        HouseService.FromMask(HouseService.ToMask(all)).Should().BeEquivalentTo(all);
        HouseService.FromMask(HouseService.ToMask([HouseAmenity.Wifi, HouseAmenity.Terrace])).Should().BeEquivalentTo([HouseAmenity.Wifi, HouseAmenity.Terrace]);
        HouseService.FromMask(0).Should().BeEmpty();
        HouseService.AllAmenities().Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Label));
    }

    [Fact]
    public void Uncovered_dates_are_the_unpriced_stretches_of_the_horizon()
    {
        var today = DateOnly.Parse("2027-01-01");
        var house = new House { PriceMode = HousePriceMode.ByDates };
        var periods = new List<PricePeriodValue>
        {
            new(DateOnly.Parse("2027-01-03"), DateOnly.Parse("2027-01-05"), 5000),
            new(DateOnly.Parse("2027-01-08"), DateOnly.Parse("2027-01-10"), 5000),
        };
        var ranges = HouseService.UncoveredDates(house, periods, today, 12);
        ranges.Select(r => (r.StartDate.ToString("MM-dd"), r.EndDate.ToString("MM-dd"))).Should().Equal(("01-01", "01-02"), ("01-06", "01-07"), ("01-11", "01-12"));
        HouseService.UncoveredDates(new House { PriceMode = HousePriceMode.Constant }, periods, today, 12).Should().BeEmpty();
    }

    [Fact]
    public void HasPrice_depends_on_mode()
    {
        var today = DateOnly.Parse("2027-01-01");
        HouseService.HasPrice(new House { PriceMode = HousePriceMode.Constant, ConstantPriceRub = 100 }, [], today).Should().BeTrue();
        HouseService.HasPrice(new House { PriceMode = HousePriceMode.Constant }, [], today).Should().BeFalse();
        HouseService.HasPrice(new House { PriceMode = HousePriceMode.ByDates, ConstantPriceRub = 100 }, [], today).Should().BeFalse("constant is ignored in ByDates");
        HouseService.HasPrice(new House { PriceMode = HousePriceMode.ByDates }, [P("2026-12-01", "2026-12-31")], today).Should().BeFalse("ended before today");
        HouseService.HasPrice(new House { PriceMode = HousePriceMode.ByDates }, [P("2026-12-01", "2027-01-01")], today).Should().BeTrue();
    }
}
