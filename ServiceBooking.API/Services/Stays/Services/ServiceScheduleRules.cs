namespace ServiceBooking.API.Services.Stays;

public sealed record WindowSpec(int StartMinute, int EndMinute);

public enum WindowError { WindowNotOnGrid, WindowEmpty, WindowOutsideBusinessDay, TooManyWindows, WindowsOverlap }

/// <summary>The verdict on the windows of one day. For <see cref="WindowError.WindowsOverlap"/> the two windows that touch are given (sorted by start).</summary>
public sealed record WindowCheck(WindowError? Error, WindowSpec? First = null, WindowSpec? Second = null)
{
    public bool Ok => Error is null;
}

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.3.3 — the windows of one business day (the weekly template and a manual date). Start/End are multiples of 30, B ≤ Start &lt; End ≤ B + 1440,
/// at most three windows, no overlap (touching is allowed). The first error by the order WindowNotOnGrid → WindowEmpty → WindowOutsideBusinessDay → TooManyWindows →
/// WindowsOverlap. Pure; vectors: service-vectors.json → windows.
/// </summary>
public static class ServiceScheduleRules
{
    public const int MaxWindowsPerDay = 3;
    public const int GridMinutes = 30;

    public static WindowCheck Validate(IReadOnlyList<WindowSpec> windows, int businessDayStartMinute = BusinessClock.DefaultBusinessDayStartMinute)
    {
        var end = businessDayStartMinute + BusinessClock.MinutesPerDay;
        if (windows.Any(w => w.StartMinute % GridMinutes != 0 || w.EndMinute % GridMinutes != 0)) return new WindowCheck(WindowError.WindowNotOnGrid);
        if (windows.Any(w => w.EndMinute <= w.StartMinute)) return new WindowCheck(WindowError.WindowEmpty);
        if (windows.Any(w => w.StartMinute < businessDayStartMinute || w.EndMinute > end)) return new WindowCheck(WindowError.WindowOutsideBusinessDay);
        if (windows.Count > MaxWindowsPerDay) return new WindowCheck(WindowError.TooManyWindows);
        var sorted = windows.OrderBy(w => w.StartMinute).ToList();
        for (var i = 1; i < sorted.Count; i++)
            if (sorted[i].StartMinute < sorted[i - 1].EndMinute) return new WindowCheck(WindowError.WindowsOverlap, sorted[i - 1], sorted[i]);
        return new WindowCheck(null);
    }

    /// <summary>The Russian 400 text of a failed check; <paramref name="dayLabel"/> names the day in an overlap message.</summary>
    public static string Message(WindowCheck check, string dayLabel, int businessDayStartMinute = BusinessClock.DefaultBusinessDayStartMinute) => check.Error switch
    {
        WindowError.WindowNotOnGrid => "Время — с шагом 30 минут",
        WindowError.WindowEmpty => "Начало окна должно быть раньше конца",
        WindowError.WindowOutsideBusinessDay =>
            $"Окно должно уложиться с {BoundaryClock(businessDayStartMinute)} до {BoundaryClock(businessDayStartMinute)} следующего дня",
        WindowError.TooManyWindows => "Не больше трёх окон в день",
        WindowError.WindowsOverlap =>
            $"Окна {ServiceTimeFormat.Window(check.First!.StartMinute, check.First.EndMinute)} и {ServiceTimeFormat.Window(check.Second!.StartMinute, check.Second.EndMinute)} ({dayLabel}) пересекаются",
        _ => string.Empty
    };

    private static string BoundaryClock(int minute) => BusinessClock.TimeOfDay(minute).ToString("HH':'mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The windows of a manual date or of the template day, sorted, as (start, end) pairs.</summary>
    public static List<WindowSpec> Sorted(IEnumerable<WindowSpec> windows) => windows.OrderBy(w => w.StartMinute).ThenBy(w => w.EndMinute).ToList();

    /// <summary>Does any window of the day contain the whole interval [startMinute, endMinute)?</summary>
    public static bool Covers(IReadOnlyList<WindowSpec> windows, int startMinute, int endMinute) =>
        windows.Any(w => w.StartMinute <= startMinute && endMinute <= w.EndMinute);
}
