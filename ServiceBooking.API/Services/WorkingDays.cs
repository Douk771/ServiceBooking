namespace ServiceBooking.API.Services;

/// <summary>
/// Working-days arithmetic for US-74's response deadline (ARCHITECTURE_CYCLE5.md §44.4) — pure, no DB,
/// no production calendar: only Saturday/Sunday are excluded, public holidays are NOT accounted for.
/// 🟡 Deliberate simplification in the SAFE direction: ignoring holidays can only make the computed
/// deadline EARLIER (stricter for the platform) than a calendar-aware count would, never later.
/// </summary>
public static class WorkingDays
{
    /// <summary>Adds <paramref name="days"/> WORKING days (Mon–Fri) to <paramref name="from"/> — the
    /// starting instant itself does not count as day zero of the count, only full days added after it.</summary>
    public static DateTime Add(DateTime from, int days)
    {
        if (days < 0) throw new ArgumentOutOfRangeException(nameof(days), "days must not be negative.");

        var result = from;
        var remaining = days;
        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                remaining--;
        }
        return result;
    }

    /// <summary>The single working day immediately preceding <paramref name="date"/> (Mon–Fri only) —
    /// the proper inverse of "one working day before X", used by due-soon matching. Unlike
    /// <see cref="Add"/> in the forward direction, this function IS injective per due date: exactly one
    /// calendar day maps to any given <paramref name="date"/>, which is what makes day-granularity
    /// idempotency (matching "today == PreviousWorkingDay(due date)") correct instead of firing on
    /// Friday, Saturday AND Sunday alike for a Monday due date.</summary>
    public static DateTime PreviousWorkingDay(DateTime date)
    {
        var result = date;
        do
        {
            result = result.AddDays(-1);
        } while (result.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return result;
    }
}
