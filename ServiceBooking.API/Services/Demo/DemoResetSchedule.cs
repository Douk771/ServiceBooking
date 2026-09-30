namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §580 — pure "is the nightly demo reset due" rule. The slot is the latest occurrence, in the configured time zone, of the
/// configured local time that is not in the future; a reset is due when the last one happened before that slot. A missed slot (the machine was down at
/// 04:00) is made up at the next tick, and a second tick after the reset finds nothing due.
///
/// A demo that has NEVER been reset (no stamp) is not due: on a fresh database the showcase is created by the operator's first <c>ops demo reset</c>, never
/// automatically at start (SPEC §3, ARCHITECTURE_CYCLE28.md §580 "Первый запуск").
/// </summary>
public static class DemoResetSchedule
{
    /// <summary>The latest slot instant (UTC) that is at or before <paramref name="nowUtc"/>.</summary>
    public static DateTime LatestSlotUtc(DateTime nowUtc, TimeOnly localTime, TimeZoneInfo zone)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var slotLocal = DateTime.SpecifyKind(localNow.Date.Add(localTime.ToTimeSpan()), DateTimeKind.Unspecified);
        if (slotLocal > localNow) slotLocal = slotLocal.AddDays(-1);
        // A local time that does not exist (DST gap) is moved forward by the zone rules.
        if (zone.IsInvalidTime(slotLocal)) slotLocal = slotLocal.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(slotLocal, zone);
    }

    public static bool IsDue(DateTime nowUtc, DateTime? lastResetUtc, TimeOnly localTime, TimeZoneInfo zone) =>
        lastResetUtc is { } last && last < LatestSlotUtc(nowUtc, localTime, zone);
}
