namespace ServiceBooking.API.Services.Scheduling;

/// <summary>Result of one run of a scheduled task, in the numbers the runner logs and persists
/// (US-21 p.6): how many rows were looked at, how many were acted on, how many bytes were freed, and a
/// human-readable one-line summary for <see cref="ServiceBooking.Core.Entities.ScheduledTaskState.LastSummary"/>.</summary>
public readonly record struct ScheduledTaskOutcome(int Scanned, int Affected, long BytesFreed, string Summary);

/// <summary>
/// Contract for one periodic background task (US-21 p.1). Adding a new task is exactly this: a new class
/// implementing this interface plus one DI registration line — the runner (<see cref="ScheduledTaskRunner"/>)
/// never references a task by name or type, it only ever enumerates whatever was registered.
/// </summary>
public interface IScheduledTask
{
    /// <summary>Stable identifier — the key used for configuration (<c>ScheduledTasks:{Name}:*</c>) and
    /// for the advisory lock (<c>scheduled-task:{Name}</c>). Not shown to end users.</summary>
    string Name { get; }

    /// <summary>How often this task should run when no configuration overrides it.</summary>
    TimeSpan DefaultPeriod { get; }

    /// <summary>Does one pass of the task's work. Expected to work in short, independently-committed
    /// batches and to respect <paramref name="ct"/> so the runner's time budget (<c>MaxRunMinutes</c>)
    /// can end the pass early without losing already-committed progress.</summary>
    Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct);
}
