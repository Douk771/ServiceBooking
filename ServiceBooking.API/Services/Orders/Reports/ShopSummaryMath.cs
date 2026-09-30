using System.Globalization;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>ARCHITECTURE_CYCLE25.md §502 — the arithmetic and texts of the shop summary. Pure; integer percentages, half away from zero.</summary>
public static class ShopSummaryMath
{
    public const string NoValueText = "—";
    private const char Minus = '−';

    /// <summary>The share of <paramref name="count"/> in <paramref name="total"/>; null when nothing to divide by.</summary>
    public static (decimal? Share, string Text) Share(int count, int total)
    {
        if (total <= 0) return (null, NoValueText);
        var share = (decimal)count / total;
        return (Math.Round(share, 4), Percent(share * 100m));
    }

    /// <summary>"3 %" — a whole percentage with a non-breaking space.</summary>
    public static string Percent(decimal percent) =>
        Math.Round(percent, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " %";

    /// <summary>Average check in the order-money rules: kopecks rounded half away from zero; null without issued orders.</summary>
    public static decimal? AverageCheck(decimal issuedAmount, int issuedCount)
    {
        if (issuedCount <= 0) return null;
        var kopecks = OrderMoney.ToKopecks(issuedAmount);
        return OrderMoney.FromKopecks((long)Math.Round((decimal)kopecks / issuedCount, 0, MidpointRounding.AwayFromZero));
    }

    public static string AverageCheckText(decimal? average) => average is { } a ? OrderTexts.Money(a) : NoValueText;

    /// <summary>"+12 %", "−3 %", "0 %"; "—" when the previous value is zero (nothing to compare with).</summary>
    public static string Delta(decimal current, decimal previous)
    {
        if (previous == 0m) return NoValueText;
        var percent = Math.Round((current - previous) / previous * 100m, 0, MidpointRounding.AwayFromZero);
        if (percent == 0m) return "0 %";
        return percent > 0
            ? $"+{percent.ToString("0", CultureInfo.InvariantCulture)} %"
            : $"{Minus}{Math.Abs(percent).ToString("0", CultureInfo.InvariantCulture)} %";
    }

    public static string Delta(int current, int previous) => Delta((decimal)current, previous);

    /// <summary>"из них в работе: 5".</summary>
    public static string InProgressText(int count) => $"из них в работе: {count}";

    public const string PaymentNote = "Оплата на месте, платформа её не видит";
}
