using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>What the rules need to know about a house.</summary>
public sealed record HouseFacts(
    int Capacity, bool ExtraBedsEnabled, int ExtraBedsMax, int ExtraBedPriceRub, bool DogsForbidden, bool HasCot, HousePriceMode PriceMode, int? ConstantPriceRub)
{
    public static HouseFacts Of(House h) => new(h.Capacity, h.ExtraBedsEnabled, h.ExtraBedsMax, h.ExtraBedPriceRub, h.DogsForbidden, h.HasCot, h.PriceMode, h.ConstantPriceRub);
}

/// <summary>What the rules need to know about the company's settings.</summary>
public sealed record SettingsFacts(
    int MinNights, int MaxNights, int HorizonDays, bool AllowGapFill, bool AllowSameDayCheckIn, int DogFeeRub, int CotFeeRub, int PrepayPercent)
{
    public static SettingsFacts Of(StaysSettings s) => new(s.MinNights, s.MaxNights, s.HorizonDays, s.AllowGapFill, s.AllowSameDayCheckIn, s.DogFeeRub, s.CotFeeRub, s.PrepayPercent);

    public StayRulesSettings Rules => new(MinNights, MaxNights, HorizonDays, AllowGapFill, AllowSameDayCheckIn);
}

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.6.2 — the rules of ONE stay as a pure function: dates (with the occupancy already loaded), guests, prices, money.
/// The same code answers the quote, the creation, the manual booking and every house of the catalog.
/// </summary>
public static class StayEvaluator
{
    public const int MaxNightsComputed = 400;

    public static StayEvaluation Evaluate(
        HouseFacts house, SettingsFacts settings, IReadOnlyList<PricePeriodValue> periods, IReadOnlyList<OccupiedPeriod> occupancies,
        StayStayInput input, DateOnly today, DateTime nowUtc, bool manual)
    {
        var rules = settings.Rules;
        var problems = new List<StayProblem>();
        var nights = input.CheckOut.DayNumber - input.CheckIn.DayNumber;

        var dateRefusal = StayRules.CheckStay(input.CheckIn, input.CheckOut, today, nowUtc, rules, occupancies, manual);
        if (dateRefusal is { } d)
            problems.Add(new StayProblem(d, StaysTexts.RefusalMessage(d, settings.MaxNights, settings.MinNights, StayRules.LastBookableNight(today, settings.HorizonDays))));

        var guests = GuestRules.Check(input.Adults, input.Children, input.Dogs, input.NeedCot, house.Capacity, house.ExtraBedsEnabled, house.ExtraBedsMax, house.DogsForbidden, house.HasCot);
        if (guests.Refusal is { } g)
            problems.Add(new StayProblem(g, StaysTexts.RefusalMessage(g, maxGuests: house.Capacity + (house.ExtraBedsEnabled ? house.ExtraBedsMax : 0), withExtraBeds: house.ExtraBedsEnabled)));

        IReadOnlyList<NightPrice> nightPrices = [];
        if (nights is > 0 and <= MaxNightsComputed)
        {
            var priced = HousePricing.NightPrices(house.PriceMode, house.ConstantPriceRub, periods, input.CheckIn, input.CheckOut);
            if (priced is null) problems.Add(new StayProblem(StayRefusalCode.NoPriceForNights, StaysTexts.RefusalMessage(StayRefusalCode.NoPriceForNights)));
            else nightPrices = priced;
        }

        StayQuoteResult? money = null;
        if (guests.Refusal is null && nightPrices.Count > 0 && nights == nightPrices.Count)
            money = StayMoney.Quote(new StayMoneyInput(
                nightPrices.Select(p => p.PriceRub).ToList(), house.Capacity, house.ExtraBedsEnabled, house.ExtraBedsMax, house.ExtraBedPriceRub,
                house.DogsForbidden, house.HasCot, settings.DogFeeRub, settings.CotFeeRub, settings.PrepayPercent, input.Adults, input.Children, input.Dogs, input.NeedCot));
        return new StayEvaluation(problems, money, nightPrices, today, rules);
    }
}
