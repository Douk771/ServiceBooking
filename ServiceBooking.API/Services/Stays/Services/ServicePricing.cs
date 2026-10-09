namespace ServiceBooking.API.Services.Stays;

public sealed record PriceRuleSpec(int DaysMask, int FromHour, int ToHour, int PriceRub);

public enum PriceRuleError { NoDays, HoursOutOfRange, PriceOutOfRange, PriceRuleOverlap }

public sealed record PriceRuleCheck(PriceRuleError? Error, Guid? ConflictingRuleId = null)
{
    public bool Ok => Error is null;
}

/// <summary>ARCHITECTURE_CYCLE39.md §39.3.3 — validation of one price rule against the others of the service. Pure; vectors: service-vectors.json → priceRules.</summary>
public static class ServicePriceRules
{
    public const int MaxRulesPerService = 50;
    public const int MaxPriceRub = 100_000;

    public static PriceRuleCheck Validate(
        PriceRuleSpec rule, IEnumerable<(Guid Id, PriceRuleSpec Rule)> existing, Guid? ignoreId = null,
        int businessDayStartMinute = BusinessClock.DefaultBusinessDayStartMinute)
    {
        if ((rule.DaysMask & 0x7F) == 0 || (rule.DaysMask & ~0x7F) != 0) return new PriceRuleCheck(PriceRuleError.NoDays);
        var firstHour = businessDayStartMinute / 60;
        if (rule.FromHour < firstHour || rule.ToHour > firstHour + 24 || rule.FromHour >= rule.ToHour) return new PriceRuleCheck(PriceRuleError.HoursOutOfRange);
        if (rule.PriceRub is < 1 or > MaxPriceRub) return new PriceRuleCheck(PriceRuleError.PriceOutOfRange);
        foreach (var (id, other) in existing)
        {
            if (id == ignoreId) continue;
            if ((other.DaysMask & rule.DaysMask) != 0 && rule.FromHour < other.ToHour && other.FromHour < rule.ToHour)
                return new PriceRuleCheck(PriceRuleError.PriceRuleOverlap, id);
        }
        return new PriceRuleCheck(null);
    }

    public static string Message(PriceRuleError error, string? conflictingLabel = null, int? conflictingPriceRub = null) => error switch
    {
        PriceRuleError.NoDays => "Выберите дни недели",
        PriceRuleError.HoursOutOfRange => "Часы — с 06:00 до 06:00 следующего дня",
        PriceRuleError.PriceOutOfRange => "Цена за час — от 1 до 100 000 ₽",
        _ => $"Правило пересекается с «{conflictingLabel}» ({conflictingPriceRub} ₽/ч)"
    };
}

/// <summary>ARCHITECTURE_CYCLE39.md §39.6.1 — the price of every hour of a session. Pure; vectors: service-vectors.json → price.</summary>
public static class ServicePricing
{
    /// <summary>
    /// Hour k (0…hours−1) starts at minute <c>startMinute + 60k</c> of the business date; its price is the rule that has the bit of the weekday OF THE BUSINESS DATE
    /// and contains that minute. No rule → null (the start is not available). An hour that starts after midnight still follows the rules of the business date.
    /// </summary>
    public static int?[] HourPrices(IReadOnlyList<PriceRuleSpec> rules, DateOnly businessDate, int startMinute, int hours)
    {
        var bit = 1 << (BusinessClock.DayOfWeekIso(businessDate) - 1);
        var result = new int?[hours];
        for (var k = 0; k < hours; k++)
        {
            var m = startMinute + 60 * k;
            foreach (var r in rules)
            {
                if ((r.DaysMask & bit) == 0 || m < r.FromHour * 60 || m >= r.ToHour * 60) continue;
                result[k] = r.PriceRub;
                break;
            }
        }
        return result;
    }

    public static bool AllPriced(int?[] prices) => prices.All(p => p is not null);
}

public sealed record ServiceItemQuantity(int UnitPriceRub, int Quantity);

public sealed record ServiceQuoteResult(int ServiceAmountRub, int ItemsAmountRub, int TotalRub, int PrepayRub, int DueOnSiteRub, int FirstHourRub);

/// <summary>ARCHITECTURE_CYCLE39.md §39.6.2 — the money of a session, whole roubles. The prepayment is taken from the SERVICE amount only (items are paid on site), half rounds up.</summary>
public static class ServiceMoney
{
    public static ServiceQuoteResult Quote(IReadOnlyList<int> hourPrices, IReadOnlyList<ServiceItemQuantity> items, int? prepayPercent)
    {
        long service = hourPrices.Sum(p => (long)p);
        long itemsSum = items.Sum(i => (long)i.UnitPriceRub * i.Quantity);
        var total = service + itemsSum;
        var prepay = prepayPercent is { } pct ? (service * pct + 50) / 100 : 0;
        return new ServiceQuoteResult((int)service, (int)itemsSum, (int)total, (int)prepay, (int)(total - prepay), hourPrices.Count == 0 ? 0 : hourPrices[0]);
    }
}
