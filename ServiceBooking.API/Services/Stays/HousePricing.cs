using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>A price period; <see cref="EndDate"/> is INCLUSIVE (unlike occupancy periods).</summary>
public readonly record struct PricePeriodValue(DateOnly StartDate, DateOnly EndDate, int PriceRub);

/// <summary>ARCHITECTURE_CYCLE37.md §37.6.1 — the price of one night. The night is priced by the date it STARTS on (Р6).</summary>
public static class HousePricing
{
    public static int? PriceFor(HousePriceMode mode, int? constantPriceRub, IReadOnlyList<PricePeriodValue> periods, DateOnly date)
    {
        if (mode == HousePriceMode.Constant) return constantPriceRub;

        // A one-day period overrides the long one that contains it (SPEC §4.4).
        foreach (var p in periods)
            if (p.StartDate == p.EndDate && p.StartDate == date) return p.PriceRub;
        foreach (var p in periods)
            if (p.StartDate != p.EndDate && p.StartDate <= date && date <= p.EndDate) return p.PriceRub;
        return null;
    }

    /// <summary>The prices of every night of [checkIn, checkOut); the whole result is null when at least one night has no price.</summary>
    public static IReadOnlyList<NightPrice>? NightPrices(
        HousePriceMode mode, int? constantPriceRub, IReadOnlyList<PricePeriodValue> periods, DateOnly checkIn, DateOnly checkOut)
    {
        var result = new List<NightPrice>();
        for (var d = checkIn; d < checkOut; d = d.AddDays(1))
        {
            var price = PriceFor(mode, constantPriceRub, periods, d);
            if (price is null) return null;
            result.Add(new NightPrice(d, price.Value));
        }
        return result;
    }

    /// <summary>The cheapest night of a house from <paramref name="today"/> on (catalog "от"); null when nothing is priced.</summary>
    public static int? PriceFrom(HousePriceMode mode, int? constantPriceRub, IReadOnlyList<PricePeriodValue> periods, DateOnly today)
    {
        if (mode == HousePriceMode.Constant) return constantPriceRub;
        int? min = null;
        foreach (var p in periods)
        {
            if (p.EndDate < today) continue;
            if (min is null || p.PriceRub < min) min = p.PriceRub;
        }
        return min;
    }
}

public readonly record struct NightPrice(DateOnly Date, int PriceRub);
