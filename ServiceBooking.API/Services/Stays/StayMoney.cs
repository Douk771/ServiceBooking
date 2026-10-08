using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public sealed record StayChargeLine(StayChargeKind Kind, string Label, int Quantity, int UnitPriceRub, int Nights, int AmountRub, bool PrepayEligible);

public sealed record StayMoneyInput(
    IReadOnlyList<int> NightPrices, int Capacity, bool ExtraBedsEnabled, int ExtraBedsMax, int ExtraBedPriceRub,
    bool DogsForbidden, bool HasCot, int DogFeeRub, int CotFeeRub, int PrepayPercent,
    int Adults, int Children, int Dogs, bool NeedCot);

public sealed record StayQuoteResult(
    StayRefusalCode? Error, int ExtraBeds, IReadOnlyList<StayChargeLine> Lines,
    int TotalRub, int PrepayRub, int DueAtCheckInRub, int AverageNightRub, int FirstNightRub);

/// <summary>ARCHITECTURE_CYCLE37.md §37.6.3 — the amount of a booking, in whole roubles. Pure; vectors: stay-vectors.json → money.</summary>
public static class StayMoney
{
    public static StayQuoteResult Quote(StayMoneyInput i)
    {
        var guest = GuestRules.Check(i.Adults, i.Children, i.Dogs, i.NeedCot, i.Capacity, i.ExtraBedsEnabled, i.ExtraBedsMax, i.DogsForbidden, i.HasCot);
        if (guest.Refusal is { } err) return new StayQuoteResult(err, guest.ExtraBeds, [], 0, 0, 0, 0, 0);

        var n = i.NightPrices.Count;
        long nightsSum = i.NightPrices.Sum(p => (long)p);
        var lines = new List<StayChargeLine>
        {
            new(StayChargeKind.Nights, $"{n} {StaysTexts.Plural(n, "ночь", "ночи", "ночей")}", n, 0, n, (int)nightsSum, true)
        };
        if (guest.ExtraBeds > 0)
            lines.Add(new(StayChargeKind.ExtraBeds, $"Доп. место × {guest.ExtraBeds}", guest.ExtraBeds, i.ExtraBedPriceRub, n, guest.ExtraBeds * i.ExtraBedPriceRub * n, false));
        if (i.Dogs > 0)
            lines.Add(new(StayChargeKind.Dogs, $"Собаки × {i.Dogs}", i.Dogs, i.DogFeeRub, n, i.Dogs * i.DogFeeRub * n, false));
        if (i.NeedCot)
            lines.Add(new(StayChargeKind.Cot, "Детская кроватка", 1, i.CotFeeRub, n, i.CotFeeRub * n, false));

        var total = lines.Sum(l => l.AmountRub);
        var prepay = PrepayOf(nightsSum, i.PrepayPercent);
        var average = n == 0 ? 0 : (int)((nightsSum + n / 2) / n);
        return new StayQuoteResult(null, guest.ExtraBeds, lines, total, prepay, total - prepay, average, n == 0 ? 0 : i.NightPrices[0]);
    }

    /// <summary>Prepayment = round-half-up(eligible sum × percent / 100).</summary>
    public static int PrepayOf(long eligibleSumRub, int percent) => (int)((eligibleSumRub * percent + 50) / 100);
}
