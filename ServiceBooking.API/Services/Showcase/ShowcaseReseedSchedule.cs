namespace ServiceBooking.API.Services.Showcase;

/// <summary>Section <c>Showcase:Reseed</c> (ARCHITECTURE_CYCLE28.md §575.7). Off by default: the production showcase is re-seeded by the operator's command.</summary>
public sealed class ShowcaseReseedOptions
{
    public const string SectionName = "Showcase:Reseed";

    public bool Enabled { get; set; }
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>Local time of the slot, <c>HH:mm</c>, in <see cref="TimeZoneId"/>.</summary>
    public string LocalTime { get; set; } = "04:30";

    public string TimeZoneId { get; set; } = "Europe/Moscow";

    public bool TryGetLocalTime(out TimeOnly time) => TimeOnly.TryParseExact(LocalTime, "HH:mm", out time);
}

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.7 — pure "is a weekly re-seed due" rule. The slot is the most recent occurrence, in the configured time zone, of the configured weekday
/// and local time that is not in the future; a re-seed is due when the last one happened before that slot. A missed slot (the machine was down) is
/// made up at the next tick, and a second tick after the re-seed finds nothing due.
/// </summary>
public static class ShowcaseReseedSchedule
{
    /// <summary>The latest slot instant (UTC) that is at or before <paramref name="nowUtc"/>.</summary>
    public static DateTime LatestSlotUtc(DateTime nowUtc, DayOfWeek day, TimeOnly localTime, TimeZoneInfo zone)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var daysBack = ((int)localNow.DayOfWeek - (int)day + 7) % 7;
        var slotLocal = DateTime.SpecifyKind(localNow.Date.AddDays(-daysBack).Add(localTime.ToTimeSpan()), DateTimeKind.Unspecified);
        if (slotLocal > localNow) slotLocal = slotLocal.AddDays(-7);
        // A local time that does not exist (DST gap) is moved forward by the zone rules; a repeated one takes the first occurrence.
        if (zone.IsInvalidTime(slotLocal)) slotLocal = slotLocal.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(slotLocal, zone);
    }

    public static bool IsDue(DateTime nowUtc, DateTime? lastReseedUtc, DayOfWeek day, TimeOnly localTime, TimeZoneInfo zone) =>
        lastReseedUtc is null || lastReseedUtc.Value < LatestSlotUtc(nowUtc, day, localTime, zone);
}
