using System.Globalization;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.3.4 — every time label of a service. A guest ALWAYS reads calendar dates (ЮР39-8: the words «бизнес-день» and «часы 6…30» are never
/// shown to a guest); the staff reads the form of SPEC §4.9 on the business date of the start. The TS twin (dom/src/utils/serviceTimeFormat.ts) follows
/// the same vectors (service-vectors.json → format, priceRules.labels). Pure.
/// </summary>
public static class ServiceTimeFormat
{
    private static readonly string[] WeekdaysShort = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];
    private static readonly string[] Months = ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];
    private static readonly string[] IsoDaysShort = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

    public static string Weekday(DateOnly d) => WeekdaysShort[(int)d.DayOfWeek];

    private static string Capital(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static string Clock(int minute) => BusinessClock.TimeOfDay(minute).ToString("HH':'mm", CultureInfo.InvariantCulture);

    /// <summary>«пт 15 янв» — a calendar date for a guest.</summary>
    public static string DateLabel(DateOnly d) => $"{Weekday(d)} {d.Day} {Months[d.Month - 1]}";

    /// <summary>«пт 15 янв» for a business date (same text; kept apart so a caller states what it means).</summary>
    public static string BusinessDateLabel(DateOnly businessDate) => DateLabel(businessDate);

    /// <summary>«Пт, 15 янв» — the staff form of a business date.</summary>
    public static string StaffDateLabel(DateOnly d) => $"{Capital(Weekday(d))}, {d.Day} {Months[d.Month - 1]}";

    /// <summary>«сб 16 янв, 00:00» — a moment of a business date for a guest, with its CALENDAR date.</summary>
    public static string GuestMoment(DateOnly businessDate, int minute)
    {
        var d = BusinessClock.CalendarDateOf(businessDate, minute);
        return $"{DateLabel(d)}, {Clock(minute)}";
    }

    /// <summary>The guest's text of a session: «пт 15 янв, 22:00 — сб 16 янв, 01:00» / «пт 15 янв, 18:00 — 21:00».</summary>
    public static string Guest(DateOnly businessDate, int startMinute, int hours)
    {
        var endMinute = startMinute + hours * 60;
        var startDate = BusinessClock.CalendarDateOf(businessDate, startMinute);
        var endDate = BusinessClock.CalendarDateOf(businessDate, endMinute);
        var tail = endDate == startDate ? Clock(endMinute) : GuestMoment(businessDate, endMinute);
        return $"{GuestMoment(businessDate, startMinute)} — {tail}";
    }

    /// <summary>The end of a session for a guest: only the time within the start's calendar day, the whole moment after midnight («сб 16 янв, 01:00»).</summary>
    public static string GuestEnd(DateOnly businessDate, int startMinute, int hours)
    {
        var endMinute = startMinute + hours * 60;
        return BusinessClock.CalendarDateOf(businessDate, endMinute) == BusinessClock.CalendarDateOf(businessDate, startMinute)
            ? Clock(endMinute) : GuestMoment(businessDate, endMinute);
    }

    /// <summary>The staff's text on the business date of the start: «Пт, 15 янв · 22:00 – 01:00 (сб)», «Пт, 15 янв · 00:30 (ночь на сб) – 02:30».</summary>
    public static string Staff(DateOnly businessDate, int startMinute, int hours) =>
        $"{StaffDateLabel(businessDate)} · {StaffRange(businessDate, startMinute, startMinute + hours * 60)}";

    /// <summary>«22:00 – 01:00 (сб)» without the date — the time part of <see cref="Staff"/>.</summary>
    public static string StaffRange(DateOnly businessDate, int startMinute, int endMinute)
    {
        var next = Weekday(businessDate.AddDays(1));
        var start = startMinute >= BusinessClock.MinutesPerDay ? $"{Clock(startMinute)} (ночь на {next})" : Clock(startMinute);
        var end = endMinute >= BusinessClock.MinutesPerDay && startMinute < BusinessClock.MinutesPerDay ? $"{Clock(endMinute)} ({next})" : Clock(endMinute);
        return $"{start} – {end}";
    }

    /// <summary>«22:00» or «00:30 (ночь на сб)» — a start in the list of a chosen date.</summary>
    public static string StartLabel(DateOnly businessDate, int startMinute) =>
        startMinute >= BusinessClock.MinutesPerDay ? $"{Clock(startMinute)} (ночь на {Weekday(businessDate.AddDays(1))})" : Clock(startMinute);

    /// <summary>«01:30 (сб)» — the end of the preparation, staff form.</summary>
    public static string StaffMoment(DateOnly businessDate, int minute) =>
        minute >= BusinessClock.MinutesPerDay ? $"{Clock(minute)} ({Weekday(businessDate.AddDays(1))})" : Clock(minute);

    /// <summary>«18:00 – 02:00 (след. дня)» — a window in the editor.</summary>
    public static string Window(int startMinute, int endMinute) => $"{EditorMoment(startMinute)} – {EditorMoment(endMinute)}";

    private static string EditorMoment(int minute) => minute >= BusinessClock.MinutesPerDay ? $"{Clock(minute)} (след. дня)" : Clock(minute);

    /// <summary>«Пн–Пт», «Пт», «Пн, Ср, Пт» — days of a rule's mask (bit 0 = Monday).</summary>
    public static string DaysLabel(int daysMask)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < 7)
        {
            if ((daysMask & (1 << i)) == 0) { i++; continue; }
            var j = i;
            while (j + 1 < 7 && (daysMask & (1 << (j + 1))) != 0) j++;
            parts.Add(j == i ? IsoDaysShort[i] : $"{IsoDaysShort[i]}–{IsoDaysShort[j]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    private static string RuleHour(int hour) => Clock(hour * 60);

    /// <summary>A price rule for a guest: «Пт 18:00 — 02:00 (ночь на сб)», «Пн–Пт 10:00 — 18:00».</summary>
    public static string RuleGuest(int daysMask, int fromHour, int toHour)
    {
        var single = System.Numerics.BitOperations.PopCount((uint)daysMask) == 1;
        var nightDay = single ? $"ночь на {Weekday(DateOnlyOfIsoDay(System.Numerics.BitOperations.TrailingZeroCount((uint)daysMask) + 1).AddDays(1))}" : "ночью";
        var start = fromHour >= 24 ? $"{RuleHour(fromHour)} ({nightDay})" : RuleHour(fromHour);
        var end = toHour >= 24 && fromHour < 24 ? $"{RuleHour(toHour)} ({nightDay})" : RuleHour(toHour);
        return $"{DaysLabel(daysMask)} {start} — {end}";
    }

    /// <summary>A price rule for the staff: «Пт 18:00 – 02:00 (след. дня)».</summary>
    public static string RuleStaff(int daysMask, int fromHour, int toHour) =>
        $"{DaysLabel(daysMask)} {Window(fromHour * 60, toHour * 60)}";

    /// <summary>«01:00 (след. дня)» — an hour of the price matrix.</summary>
    public static string HourLabel(int hour) => EditorMoment(hour * 60);

    public static string IsoDayName(int isoDay) => isoDay switch
    {
        1 => "Понедельник", 2 => "Вторник", 3 => "Среда", 4 => "Четверг", 5 => "Пятница", 6 => "Суббота", _ => "Воскресенье"
    };

    // Any date that falls on the wanted ISO weekday: 2027-01-04 is a Monday.
    private static DateOnly DateOnlyOfIsoDay(int isoDay) => new DateOnly(2027, 1, 4).AddDays(isoDay - 1);
}
