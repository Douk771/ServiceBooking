namespace ServiceBooking.API.Services.Scheduling;

/// <summary>
/// Pure "is it time yet" / "has this gone quiet" arithmetic for the scheduler (US-21 pp.4,6) — no DB, no
/// clock dependency baked in (<c>nowUtc</c> is always a parameter), unit-tested directly.
/// </summary>
public static class ScheduledTaskSchedule
{
    /// <summary>
    /// Whether a task is due to run again. Never run before (<paramref name="lastStartedAtUtc"/> is
    /// null) is always due. Deliberately keyed off the last START, not the last successful finish: a
    /// task that failed is retried on its NORMAL schedule, not immediately — SPEC US-21 p.3 ("marked
    /// unsuccessful and scheduled for its next run on the usual schedule"), not a backoff/retry policy.
    /// </summary>
    public static bool IsDue(DateTime? lastStartedAtUtc, TimeSpan period, DateTime nowUtc) =>
        lastStartedAtUtc is null || lastStartedAtUtc.Value + period <= nowUtc;

    /// <summary>
    /// Whether the task looks dead: never finished, or its last finish is further in the past than two
    /// of its own periods (US-21 p.6 — the answer to "is the background component alive?", surfaced by
    /// GET /api/admin/scheduled-tasks).
    /// </summary>
    public static bool IsOverdue(DateTime? lastFinishedAtUtc, TimeSpan period, DateTime nowUtc) =>
        lastFinishedAtUtc is null || lastFinishedAtUtc.Value < nowUtc - (period * 2);
}
