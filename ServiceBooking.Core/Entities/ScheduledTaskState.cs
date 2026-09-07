namespace ServiceBooking.Core.Entities;

/// <summary>Persisted state of one periodic background task (US-21). Keyed by task name so "when did
/// this last run" survives an application restart without a separate schedule table — read whole by
/// both the runner (deciding whether it's time) and GET /api/admin/scheduled-tasks (liveness).</summary>
public class ScheduledTaskState
{
    public string Name { get; set; } = string.Empty; // PK; == IScheduledTask.Name
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastFinishedAtUtc { get; set; }
    public bool LastSucceeded { get; set; }
    public int LastDurationMs { get; set; }
    public string? LastSummary { get; set; }  // e.g. "scanned 812, deleted 37, freed 11.4 MB"
    public string? LastError { get; set; }    // message + type, truncated to 2000 chars
}
