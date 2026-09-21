namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// The single place notification-related time arithmetic happens (ARCHITECTURE_CYCLE4.md §34.1). Pure:
/// no DB, no clock of its own — every "now" is a parameter.
/// </summary>
public static class NotificationTiming
{
    /// <summary>
    /// Converts a booking's local date/time to UTC using the company's IANA zone. The date and start
    /// time are interpreted as wall-clock time in that zone — Kind is always Unspecified going in, which
    /// is what <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> requires. None of the
    /// zones this cycle seeds observe DST (Russia abolished it in 2011/2014), so
    /// <see cref="TimeZoneInfo.IsInvalidTime"/>/<see cref="TimeZoneInfo.IsAmbiguousTime(DateTime)"/>
    /// should never actually trigger — but a background task must not throw over a hypothetical future
    /// zone change, so an invalid local time is nudged forward by exactly its own gap instead of raising.
    /// </summary>
    public static DateTime ComputeVisitStartUtc(DateOnly date, TimeOnly startTime, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = DateTime.SpecifyKind(date.ToDateTime(startTime), DateTimeKind.Unspecified);

        if (tz.IsInvalidTime(local))
        {
            var rule = tz.GetAdjustmentRules().FirstOrDefault(r => r.DateStart <= local && local < r.DateEnd);
            var gap = rule?.DaylightDelta ?? TimeSpan.FromHours(1);
            local = local.Add(gap.Duration());
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    /// <summary>
    /// Deterministic jitter in the range [-<paramref name="maxJitterMinutes"/>, +<paramref
    /// name="maxJitterMinutes"/>], derived from the row's own id so the same row always recomputes the
    /// same due time (no test flakiness, no drift on a retried computation).
    /// <see cref="Guid.GetHashCode"/> is deterministic across runs (unlike <c>string.GetHashCode</c>,
    /// Guid hashing isn't salted per-process), which is what makes this safe to rely on.
    /// </summary>
    public static int JitterMinutes(Guid id, int maxJitterMinutes)
    {
        if (maxJitterMinutes <= 0) return 0;
        var range = maxJitterMinutes * 2 + 1;
        var raw = id.GetHashCode() % range;
        var normalized = raw < 0 ? raw + range : raw;
        return normalized - maxJitterMinutes;
    }

    /// <summary>Reminder due time: visit start minus the configured lead, spread by jitter so many
    /// reminders due at the same lead time don't all become due in the same instant.</summary>
    public static DateTime ComputeReminderDueAtUtc(
        Guid notificationId, DateTime visitStartUtc, int reminderLeadMinutes, int maxJitterMinutes) =>
        visitStartUtc.AddMinutes(-reminderLeadMinutes).AddMinutes(JitterMinutes(notificationId, maxJitterMinutes));

    /// <summary>Whether the visit this notification is about has already started — the sole rule for the
    /// <c>Expired</c> terminal status (US-28 p.8, §23.4): protects against sending a reminder/confirmation
    /// after the fact, independent of whether the message was ever attempted.</summary>
    public static bool IsExpired(DateTime visitStartUtc, DateTime nowUtc) => visitStartUtc <= nowUtc;

    /// <summary>Whether there is too little time left before the visit to bother sending (US-31 p.2) —
    /// the guard that stops a queue drained after a long channel outage from blasting same-minute
    /// reminders for visits that already effectively need no reminder.</summary>
    public static bool IsBelowMinimumLeadTime(DateTime visitStartUtc, DateTime nowUtc, int minLeadMinutes) =>
        (visitStartUtc - nowUtc).TotalMinutes < minLeadMinutes;
}
