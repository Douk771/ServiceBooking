namespace ServiceBooking.API.Services.Shops;

/// <summary>The monthly order limit as a screen prints it (OrderLimitDto).</summary>
public sealed record OrderLimitInfo(int Used, int? Limit, string MonthLabel, OrderLimitWarningLevel WarningLevel, string? Text);

/// <summary>
/// ARCHITECTURE_CYCLE24.md §459.6 — the 80 % / 100 % thresholds and the limit texts, pure. Warning80 starts at <c>ceil(0.8 × limit)</c> and
/// lasts while the limit is not reached; "reached" is <c>used ≥ limit</c>.
/// </summary>
public static class OrderLimitRules
{
    /// <summary>ceil(0.8 × limit) in integer arithmetic (floating point would turn 15 × 0.8 into 12.000000000000002).</summary>
    public static int Warning80Threshold(int limit) => (limit * 4 + 4) / 5;

    public static OrderLimitWarningLevel Level(int used, int? limit)
    {
        if (limit is not { } l) return OrderLimitWarningLevel.None;
        if (used >= l) return OrderLimitWarningLevel.Reached;
        return used >= Warning80Threshold(l) ? OrderLimitWarningLevel.Warning80 : OrderLimitWarningLevel.None;
    }

    public static OrderLimitInfo Describe(int used, int? limit, DateOnly anyDayOfMonth) => new(
        used, limit, ShopTimeTexts.MonthLabel(anyDayOfMonth), Level(used, limit),
        limit is null ? null : $"Заказов в этом месяце: {used} из {limit}");

    /// <summary>"Лимит заказов на октябрь исчерпан (150 из 150). Новые заказы примутся с 1 ноября или после смены тарифа".</summary>
    public static string ReachedOwnerText(int used, int limit, DateOnly anyDayOfMonth) =>
        $"Лимит заказов на {ShopTimeTexts.MonthName(anyDayOfMonth)} исчерпан ({used} из {limit}). " +
        $"Новые заказы примутся с {ShopTimeTexts.FirstOfNextMonth(anyDayOfMonth)} или после смены тарифа";
}
