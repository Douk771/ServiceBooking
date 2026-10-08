namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.3.2 — the business day of a time-slot service. A business day D lasts from <c>D 00:00 + B</c> to <c>D+1 00:00 + B</c> in the company's
/// time zone (B = <see cref="DefaultBusinessDayStartMinute"/>, 06:00): a session that starts at 01:00 on Saturday belongs to Friday's schedule. The time of day of a
/// service is stored as MINUTES from 00:00 of the business date (0…2880; 1560 = 02:00 of the next calendar day). Pure; vectors: service-vectors.json → businessDay.
/// </summary>
public static class BusinessClock
{
    public const int DefaultBusinessDayStartMinute = 360;
    public const int MinutesPerDay = 1440;

    /// <summary>The business date and the minute of that date (B ≤ minute &lt; B + 1440) for an instant.</summary>
    public static (DateOnly Date, int Minute) BusinessDateOf(string timeZoneId, DateTime utc, int businessDayStartMinute = DefaultBusinessDayStartMinute)
    {
        var local = StayTime.LocalDateTime(timeZoneId, utc);
        var date = DateOnly.FromDateTime(local.AddMinutes(-businessDayStartMinute));
        var minute = (int)(local - date.ToDateTime(TimeOnly.MinValue)).TotalMinutes;
        return (date, minute);
    }

    /// <summary>The instant at which the local wall clock shows <c>date 00:00 + minute</c>.</summary>
    public static DateTime ToUtc(string timeZoneId, DateOnly date, int minute) =>
        StayTime.ToUtc(timeZoneId, date, TimeOnly.MinValue).AddMinutes(minute);

    /// <summary>"Today" of a service schedule: at 01:00 on Saturday it is still Friday.</summary>
    public static DateOnly TodayBusinessDate(string timeZoneId, DateTime nowUtc, int businessDayStartMinute = DefaultBusinessDayStartMinute) =>
        BusinessDateOf(timeZoneId, nowUtc, businessDayStartMinute).Date;

    /// <summary>ISO day of week of the business date (1 = Monday … 7 = Sunday).</summary>
    public static int DayOfWeekIso(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    /// <summary>The calendar date (not the business date) a minute of a business date falls on.</summary>
    public static DateOnly CalendarDateOf(DateOnly businessDate, int minute) => businessDate.AddDays((int)Math.Floor(minute / (double)MinutesPerDay));

    public static TimeOnly TimeOfDay(int minute) => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(((minute % MinutesPerDay) + MinutesPerDay) % MinutesPerDay));
}
