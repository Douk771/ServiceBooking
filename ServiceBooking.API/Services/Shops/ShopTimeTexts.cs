using System.Globalization;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §449, API_CONTRACT_CYCLE24.md §470 — the small formatting helpers every time text of a shop is
/// built from. Pure. "H:mm" without a leading zero in sentences ("до 21:00", "в 9:00"), "HH:mm" with it in fields and slot labels.
/// </summary>
public static class ShopTimeTexts
{
    private static readonly string[] DayShort = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];
    private static readonly string[] MonthShort = ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];
    private static readonly string[] MonthNominative =
        ["январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"];
    private static readonly string[] MonthGenitive =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    /// <summary>"9:00", "21:05", "0:00" — minutes of a day, wrapped modulo 24 h.</summary>
    public static string Clock(int minutes)
    {
        var m = ((minutes % 1440) + 1440) % 1440;
        return $"{m / 60}:{m % 60:00}";
    }

    /// <summary>"09:00" — the fixed-width form used in fields and slot labels.</summary>
    public static string Hhmm(int minutes)
    {
        var m = ((minutes % 1440) + 1440) % 1440;
        return $"{m / 60:00}:{m % 60:00}";
    }

    public static string DayName(DayOfWeek day) => DayShort[(int)day];

    /// <summary>"2 окт".</summary>
    public static string DayMonth(DateOnly date) => $"{date.Day} {MonthShort[date.Month - 1]}";

    /// <summary>"пт 2 окт".</summary>
    public static string DateShort(DateOnly date) => $"{DayName(date.DayOfWeek)} {DayMonth(date)}";

    /// <summary>"Сегодня" / "Завтра" / "пт 2 окт".</summary>
    public static string DateLabel(DateOnly date, DateOnly today) =>
        date == today ? "Сегодня" : date == today.AddDays(1) ? "Завтра" : DateShort(date);

    /// <summary>"октябрь 2026".</summary>
    public static string MonthLabel(DateOnly anyDayOfMonth) => $"{MonthNominative[anyDayOfMonth.Month - 1]} {anyDayOfMonth.Year}";

    /// <summary>"октябрь" — the month name in the form the "на {месяц}" phrase needs (a plain nominative for every Russian month).</summary>
    public static string MonthName(DateOnly anyDayOfMonth) => MonthNominative[anyDayOfMonth.Month - 1];

    /// <summary>"1 ноября" — the first day of the month FOLLOWING the given one.</summary>
    public static string FirstOfNextMonth(DateOnly anyDayOfMonth)
    {
        var next = new DateOnly(anyDayOfMonth.Year, anyDayOfMonth.Month, 1).AddMonths(1);
        return $"1 {MonthGenitive[next.Month - 1]}";
    }

    public static string Plural(int n, string one, string few, string many)
    {
        var mod100 = Math.Abs(n) % 100;
        var mod10 = mod100 % 10;
        if (mod100 is >= 11 and <= 14) return many;
        return mod10 == 1 ? one : mod10 is >= 2 and <= 4 ? few : many;
    }

    public static string Inv(int n) => n.ToString(CultureInfo.InvariantCulture);
}
