using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>API_CONTRACT_CYCLE25.md §522 — the report period presets; the API enum (strings in JSON).</summary>
public enum ReportPeriodPreset
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    ThisMonth,
    LastMonth,
    Custom
}

/// <summary>A resolved period: inclusive dates by <c>PickupDate</c>, the label and the length.</summary>
public sealed record ReportPeriod(ReportPeriodPreset Preset, DateOnly From, DateOnly To, string Label)
{
    public const int MaxDays = 366;
    public int Days => To.DayNumber - From.DayNumber + 1;

    public const string CustomBoundsRequired = "Укажите начало и конец периода";
    public const string EndBeforeStart = "Конец периода раньше начала";
    public const string TooLong = "Период — не длиннее 366 дней";
    public const string DateOutOfRange = "Дата должна быть между 2000 и 2100 годом";

    public static readonly DateOnly MinDate = new(2000, 1, 1);
    public static readonly DateOnly MaxDate = new(2100, 12, 31);

    /// <summary>A date the API accepts from a client: far enough from DateOnly bounds that day arithmetic never overflows.</summary>
    public static bool IsSaneDate(DateOnly date) => date >= MinDate && date <= MaxDate;

    /// <summary>
    /// ARCHITECTURE_CYCLE25.md §500 — the ONLY place periods are computed; the frontend never does. <paramref name="workingDay"/> is the shop's
    /// current working day (T-25-04). Pure. <paramref name="error"/> is the Russian 400 text.
    /// </summary>
    public static bool TryResolve(ReportPeriodPreset preset, DateOnly? from, DateOnly? to, DateOnly workingDay, out ReportPeriod period, out string? error)
    {
        error = null;
        switch (preset)
        {
            case ReportPeriodPreset.Today:
                period = new(preset, workingDay, workingDay, $"Сегодня, {ShopTimeTexts.DayMonth(workingDay)}");
                return true;
            case ReportPeriodPreset.Yesterday:
                var y = workingDay.AddDays(-1);
                period = new(preset, y, y, $"Вчера, {ShopTimeTexts.DayMonth(y)}");
                return true;
            case ReportPeriodPreset.Last7Days:
                period = Range(preset, workingDay.AddDays(-6), workingDay, "7 дней: ");
                return true;
            case ReportPeriodPreset.Last30Days:
                period = Range(preset, workingDay.AddDays(-29), workingDay, "30 дней: ");
                return true;
            case ReportPeriodPreset.ThisMonth:
                period = Month(preset, workingDay);
                return true;
            case ReportPeriodPreset.LastMonth:
                period = Month(preset, workingDay.AddMonths(-1));
                return true;
            case ReportPeriodPreset.Custom:
                if (from is not { } f || to is not { } t) { error = CustomBoundsRequired; period = null!; return false; }
                if (!IsSaneDate(f) || !IsSaneDate(t)) { error = DateOutOfRange; period = null!; return false; }
                if (t < f) { error = EndBeforeStart; period = null!; return false; }
                if (t.DayNumber - f.DayNumber + 1 > MaxDays) { error = TooLong; period = null!; return false; }
                period = new(preset, f, t, CustomLabel(f, t));
                return true;
            default:
                error = CustomBoundsRequired;
                period = null!;
                return false;
        }
    }

    /// <summary>
    /// The previous period of the same length (P1 comparison): <c>[from − len, to − len]</c>; for a calendar month — the previous calendar month whole.
    /// </summary>
    public ReportPeriod Previous()
    {
        if (Preset is ReportPeriodPreset.ThisMonth or ReportPeriodPreset.LastMonth)
        {
            var month = Month(Preset, From.AddMonths(-1));
            return month;
        }
        var len = Days;
        var from = From.AddDays(-len);
        var to = To.AddDays(-len);
        return new ReportPeriod(Preset, from, to, Preset switch
        {
            ReportPeriodPreset.Today or ReportPeriodPreset.Yesterday => ShopTimeTexts.DayMonth(from),
            ReportPeriodPreset.Custom => CustomLabel(from, to),
            _ => RangeText(from, to)
        });
    }

    private static ReportPeriod Range(ReportPeriodPreset preset, DateOnly from, DateOnly to, string prefix) =>
        new(preset, from, to, prefix + RangeText(from, to));

    /// <summary>"24–30 сен" in one month, "28 сен – 4 окт" across months.</summary>
    private static string RangeText(DateOnly from, DateOnly to) =>
        from.Year == to.Year && from.Month == to.Month
            ? $"{from.Day}–{to.Day} {MonthShort(to)}"
            : $"{ShopTimeTexts.DayMonth(from)} – {ShopTimeTexts.DayMonth(to)}";

    private static ReportPeriod Month(ReportPeriodPreset preset, DateOnly anyDay)
    {
        var first = new DateOnly(anyDay.Year, anyDay.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var label = ShopTimeTexts.MonthLabel(first);
        return new ReportPeriod(preset, first, last, char.ToUpperInvariant(label[0]) + label[1..]);
    }

    /// <summary>"15 авг – 30 сен 2026", "30 сен 2026", "15 авг 2025 – 30 сен 2026".</summary>
    private static string CustomLabel(DateOnly from, DateOnly to)
    {
        if (from == to) return $"{ShopTimeTexts.DayMonth(from)} {from.Year}";
        return from.Year == to.Year
            ? $"{ShopTimeTexts.DayMonth(from)} – {ShopTimeTexts.DayMonth(to)} {to.Year}"
            : $"{ShopTimeTexts.DayMonth(from)} {from.Year} – {ShopTimeTexts.DayMonth(to)} {to.Year}";
    }

    private static string MonthShort(DateOnly date) => ShopTimeTexts.DayMonth(date)[(ShopTimeTexts.DayMonth(date).IndexOf(' ') + 1)..];
}
