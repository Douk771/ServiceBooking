namespace ServiceBooking.API.Services.Slots;

public enum ReminderDecision { Send, Wait, Skip }

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.9.5 — when to remind a guest about a session. Pure: every time is LOCAL to the company time zone
/// (the caller converts from UTC with the company zone), so the policy needs no zone argument.
/// </summary>
public static class SessionReminderPolicy
{
    public const int DefaultGraceMinutes = 15;
    private static readonly TimeSpan DayFrom = TimeSpan.FromHours(8);
    private static readonly TimeSpan DayTo = TimeSpan.FromHours(22);

    /// <summary>start − N h, moved out of the night: [08:00, 22:00] stays; after 22:00 → 22:00 same day; before 08:00 → 08:00 if earlier than the start, else 22:00 the day before.</summary>
    public static DateTime Moment(DateTime start, int hoursBefore)
    {
        var raw = start.AddHours(-hoursBefore);
        var t = raw.TimeOfDay;
        if (t >= DayFrom && t <= DayTo) return raw;
        if (t > DayTo) return raw.Date + DayTo;
        var morning = raw.Date + DayFrom;
        return morning < start ? morning : raw.Date.AddDays(-1) + DayTo;
    }

    public static ReminderDecision Decide(DateTime moment, DateTime created, DateTime now, DateTime start, int graceMinutes = DefaultGraceMinutes)
    {
        if (created > moment) return ReminderDecision.Skip;
        if (now >= start) return ReminderDecision.Skip;
        if (now < moment) return ReminderDecision.Wait;
        if ((now - moment).TotalMinutes <= graceMinutes) return ReminderDecision.Send;

        // Late (payment confirmed later, task downtime): by day send, by night wait for 08:00 if it is before the start.
        var t = now.TimeOfDay;
        if (t >= DayFrom && t < DayTo) return ReminderDecision.Send;
        var nextMorning = t < DayFrom ? now.Date + DayFrom : now.Date.AddDays(1) + DayFrom;
        return nextMorning < start ? ReminderDecision.Wait : ReminderDecision.Skip;
    }
}
