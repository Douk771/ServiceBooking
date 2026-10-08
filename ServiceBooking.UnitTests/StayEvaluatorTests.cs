using FluentAssertions;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class StayEvaluatorTests
{
    private static readonly DateOnly Today = DateOnly.Parse("2027-01-01");
    private static readonly DateTime Now = new(2027, 1, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly HouseFacts House = new(4, true, 2, 800, false, true, HousePriceMode.Constant, 5000);
    private static readonly SettingsFacts Settings = new(2, 30, 365, false, true, 500, 0, 30);

    private static StayEvaluation Eval(string ci, string co, int adults = 2, int children = 0, int dogs = 0, bool cot = false,
        IReadOnlyList<OccupiedPeriod>? occ = null, HouseFacts? house = null, IReadOnlyList<PricePeriodValue>? periods = null, bool manual = false) =>
        StayEvaluator.Evaluate(house ?? House, Settings, periods ?? [], occ ?? [], new StayStayInput(DateOnly.Parse(ci), DateOnly.Parse(co), adults, children, dogs, cot), Today, Now, manual);

    [Fact]
    public void A_good_stay_has_no_problems_and_the_money()
    {
        var e = Eval("2027-02-01", "2027-02-04", adults: 5, dogs: 1);
        e.Problems.Should().BeEmpty();
        e.Money!.TotalRub.Should().Be(15000 + 3 * 800 + 3 * 500);
        e.Nights.Should().HaveCount(3);
    }

    [Fact]
    public void Problems_come_in_the_order_dates_guests_prices()
    {
        var e = Eval("2027-02-01", "2027-02-02", adults: 20, house: House with { PriceMode = HousePriceMode.ByDates });
        e.Problems.Select(p => p.Code).Should().Equal(StayRefusalCode.MinNightsNotMet, StayRefusalCode.TooManyGuests, StayRefusalCode.NoPriceForNights);
        e.Money.Should().BeNull();
    }

    [Fact]
    public void Messages_carry_the_numbers_of_the_rule()
    {
        Eval("2027-02-01", "2027-02-02").Problems.Single().Message.Should().Be("Минимальный срок проживания — 2 ночи");
        Eval("2027-02-01", "2027-04-01").Problems.Single().Message.Should().Be("Максимальный срок проживания — 30 ночей");
        Eval("2028-01-01", "2028-01-03").Problems.Single().Message.Should().Be("Бронирование открыто до 31.12.2027");
        Eval("2027-02-01", "2027-02-03", adults: 7).Problems.Single().Message.Should().Be("В доме помещается не больше 6 гостей, включая доп. места");
    }

    [Fact]
    public void An_occupied_night_blocks_but_an_expired_hold_does_not()
    {
        var busy = new[] { new OccupiedPeriod(DateOnly.Parse("2027-02-02"), DateOnly.Parse("2027-02-04")) };
        Eval("2027-02-01", "2027-02-03", occ: busy).Problems.Single().Code.Should().Be(StayRefusalCode.DatesUnavailable);
        var expired = new[] { new OccupiedPeriod(DateOnly.Parse("2027-02-02"), DateOnly.Parse("2027-02-04"), Now.AddMinutes(-1)) };
        Eval("2027-02-01", "2027-02-03", occ: expired).Problems.Should().BeEmpty();
    }

    [Fact]
    public void Manual_booking_ignores_minimum_and_horizon()
    {
        Eval("2027-02-01", "2027-02-02", manual: true).Problems.Should().BeEmpty();
        Eval("2029-02-01", "2029-02-02", manual: true).Problems.Should().BeEmpty();
    }

    [Fact]
    public void ByDates_prices_use_the_period_of_each_night()
    {
        var periods = new[]
        {
            new PricePeriodValue(DateOnly.Parse("2027-02-01"), DateOnly.Parse("2027-02-10"), 6000),
            new PricePeriodValue(DateOnly.Parse("2027-02-02"), DateOnly.Parse("2027-02-02"), 9000),
        };
        var e = Eval("2027-02-01", "2027-02-04", house: House with { PriceMode = HousePriceMode.ByDates }, periods: periods);
        e.Nights.Select(n => n.PriceRub).Should().Equal(6000, 9000, 6000);
        e.Money!.TotalRub.Should().Be(21000);
    }

    [Theory]
    [InlineData("79131234567", "+7 (9••) •••-••-67")]
    [InlineData("+79131234567", "+7 (9••) •••-••-67")]
    [InlineData(null, null)]
    public void Phone_mask(string? phone, string? expected) => StayPhone.Mask(phone).Should().Be(expected);
}
