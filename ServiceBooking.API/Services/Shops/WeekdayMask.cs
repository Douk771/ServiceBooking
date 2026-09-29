namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §448.1, §452.4 — the weekday mask of a product as a pure helper: bit 0 = Monday … bit 6 = Sunday
/// (ISO order, not <see cref="DayOfWeek"/>'s Sunday = 0). 127 = every day; 0 = "never by the weekly rule" (sold only through
/// a daily menu).
/// </summary>
public static class WeekdayMask
{
    public const int All = 127;

    private static readonly DayOfWeek[] IsoOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private static readonly string[] ShortNames = ["пн", "вт", "ср", "чт", "пт", "сб", "вс"];

    public static IReadOnlyList<DayOfWeek> IsoDays => IsoOrder;

    public static int Bit(DayOfWeek day) => 1 << Array.IndexOf(IsoOrder, day);

    public static bool IsValid(int mask) => mask is >= 0 and <= All;

    public static bool Allows(int mask, DayOfWeek day) => (mask & Bit(day)) != 0;

    public static bool Allows(int mask, DateOnly date) => Allows(mask, date.DayOfWeek);

    public static int FromDays(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (mask, day) => mask | Bit(day));

    /// <summary>The days of a mask, Monday first.</summary>
    public static List<DayOfWeek> ToDays(int mask) => IsoOrder.Where(d => Allows(mask, d)).ToList();

    /// <summary>"пн, ср, пт"; null when every day (nothing to show); "только по меню" for an empty mask.</summary>
    public static string? Label(int mask)
    {
        if (mask == All) return null;
        if (mask == 0) return "только по меню";
        return string.Join(", ", IsoOrder.Select((d, i) => (d, i)).Where(t => Allows(mask, t.d)).Select(t => ShortNames[t.i]));
    }
}
