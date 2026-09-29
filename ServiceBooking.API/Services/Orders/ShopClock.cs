namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.5 — the shop's own business day: the local date in the shop's time zone and the UTC
/// bounds of that day. Order numbers restart every business day; "completed today" on the board is this day.
/// </summary>
public static class ShopClock
{
    public static DateOnly BusinessDate(string timeZoneId, DateTime nowUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), zone));
    }

    /// <summary>[start, end) of the local day in UTC. Correct on DST-change days (the day is 23 or 25 hours long).</summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayBoundsUtc(string timeZoneId, DateOnly date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        DateTime ToUtc(DateOnly d)
        {
            var local = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            // A local midnight that does not exist (DST jump at 00:00) is moved to the first valid instant.
            if (zone.IsInvalidTime(local)) local = local.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
        return (ToUtc(date), ToUtc(date.AddDays(1)));
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
