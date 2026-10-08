namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.6 / R37-7 — every deadline of a stay booking is computed in the time zone captured IN the booking
/// (<c>TimeZoneIdSnapshot</c>), never in UTC and never in the server's zone. These are the only conversions the pure rules use.
/// </summary>
public static class StayTime
{
    public static DateOnly LocalDate(string timeZoneId, DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone));
    }

    public static DateTime LocalDateTime(string timeZoneId, DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone);
    }

    /// <summary>The UTC instant at which the local wall clock in <paramref name="timeZoneId"/> shows <paramref name="date"/> + <paramref name="time"/>.
    /// A wall time that does not exist (DST gap) is moved forward by an hour.</summary>
    public static DateTime ToUtc(string timeZoneId, DateOnly date, TimeOnly time)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
