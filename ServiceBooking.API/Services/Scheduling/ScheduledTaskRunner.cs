using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling;

/// <summary>
/// The single <see cref="BackgroundService"/> for the whole application (ARCHITECTURE.md §8.1). It does
/// not know the name of a single task — it resolves whatever was registered as <see cref="IScheduledTask"/>
/// via DI and ticks them all on the same loop. A second task is therefore a new class plus one
/// registration line; this file never changes for it (US-21 p.1).
/// </summary>
public sealed class ScheduledTaskRunner(
    IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ScheduledTaskRunner> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("ScheduledTasks:Enabled", true))
        {
            logger.LogInformation("Scheduled task runner is disabled (ScheduledTasks:Enabled=false)");
            return;
        }

        var tickSeconds = config.GetValue("ScheduledTasks:TickSeconds", 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(tickSeconds));

        do
        {
            // A single misbehaving tick (e.g. a transient DB outage while listing tasks) must not end
            // the loop forever — the whole point of this component is to keep trying on schedule.
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduled task runner tick failed unexpectedly");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        // Tasks are resolved fresh every tick from a throwaway scope: they are Scoped (they need an
        // AppDbContext), and the runner itself is a singleton that must never hold one open.
        using var listScope = scopeFactory.CreateScope();
        var tasks = listScope.ServiceProvider.GetServices<IScheduledTask>().ToList();

        foreach (var task in tasks)
        {
            if (stoppingToken.IsCancellationRequested) return;

            var options = ScheduledTaskOptions.For(config, task);
            if (!options.Enabled) continue;

            // "Is it time yet" is read from the DB, not memory — the whole point is to survive a
            // restart without re-running early (US-21 p.4).
            using var checkScope = scopeFactory.CreateScope();
            var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await checkDb.ScheduledTaskStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Name == task.Name, stoppingToken);

            if (!ScheduledTaskSchedule.IsDue(state?.LastStartedAtUtc, options.Period, DateTime.UtcNow)) continue;

            await RunOneAsync(task, options, stoppingToken);
        }
    }

    /// <summary>
    /// Runs one task once, with every guarantee US-21 asks for. Two SEPARATE scopes/connections are
    /// used deliberately: <paramref name="task"/> itself is resolved and executed in its own scope so its
    /// intermediate commits (batched work, US-21 p.4) don't touch the transaction that holds the
    /// advisory lock in THIS scope — pg_advisory_xact_lock lives until that transaction ends, so sharing
    /// the connection would release the lock at the task's first commit, defeating the whole point of
    /// taking it (ARCHITECTURE.md §8.3).
    /// </summary>
    private async Task RunOneAsync(IScheduledTask task, ScheduledTaskOptions options, CancellationToken stoppingToken)
    {
        using var lockScope = scopeFactory.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTx = await lockDb.Database.BeginTransactionAsync(stoppingToken);

        if (!await AdvisoryLock.TryAcquireAsync(lockDb, $"scheduled-task:{task.Name}"))
        {
            logger.LogDebug("Scheduled task {Task} skipped: another instance holds the lock", task.Name);
            await lockTx.RollbackAsync(stoppingToken);
            return;
        }

        var startedAt = DateTime.UtcNow;
        await SetStartedAsync(task.Name, startedAt, stoppingToken);
        logger.LogInformation("Scheduled task {Task} starting", task.Name);

        bool succeeded;
        string? summary = null;
        string? error = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(options.MaxRunTime);

        try
        {
            using var workScope = scopeFactory.CreateScope();
            // GetRequiredService<IScheduledTask>() would resolve only the FIRST registered
            // implementation, not necessarily this one, once a second task exists — resolve all and
            // pick by name so adding a second task never silently breaks the first.
            var workTask = workScope.ServiceProvider.GetServices<IScheduledTask>().First(t => t.Name == task.Name);

            var outcome = await workTask.ExecuteAsync(cts.Token);
            succeeded = true;
            summary = outcome.Summary;
            logger.LogInformation(
                "Scheduled task {Task} finished: scanned {Scanned}, affected {Affected}, freed {Bytes} bytes",
                task.Name, outcome.Scanned, outcome.Affected, outcome.BytesFreed);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Hit our own time budget, not a host shutdown — this is a normal, expected outcome for a
            // task with more work than fits in one pass (US-21 p.7): whatever it already committed
            // stands, and the rest continues on the next scheduled run.
            succeeded = true;
            summary = "(partial, time budget reached)";
            logger.LogInformation("Scheduled task {Task} reached its time budget and stopped early", task.Name);
        }
        catch (Exception ex)
        {
            succeeded = false;
            error = $"{ex.GetType().Name}: {ex.Message}";
            if (error.Length > 2000) error = error[..2000];
            logger.LogError(ex, "Scheduled task {Task} failed", task.Name);
        }

        await SetFinishedAsync(task.Name, startedAt, succeeded, summary, error, stoppingToken);

        // Nothing was ever written inside lockTx — it exists only to hold the advisory lock for the
        // duration of the run. Rolling back (rather than committing an empty transaction) is the correct
        // way to release it.
        await lockTx.RollbackAsync(stoppingToken);
    }

    private async Task SetStartedAsync(string taskName, DateTime startedAtUtc, CancellationToken ct)
    {
        // Deliberately its own short-lived scope/transaction, separate from the lock-holding one above:
        // this write must be visible immediately (so a concurrent GET /api/admin/scheduled-tasks sees
        // "running"), not deferred until the lock transaction eventually rolls back.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.ScheduledTaskStates.FirstOrDefaultAsync(s => s.Name == taskName, ct);
        if (state is null)
        {
            state = new ScheduledTaskState { Name = taskName };
            db.ScheduledTaskStates.Add(state);
        }
        state.LastStartedAtUtc = startedAtUtc;
        await db.SaveChangesAsync(ct);
    }

    private async Task SetFinishedAsync(
        string taskName, DateTime startedAtUtc, bool succeeded, string? summary, string? error, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.ScheduledTaskStates.FirstOrDefaultAsync(s => s.Name == taskName, ct);
        if (state is null) return; // SetStartedAsync always creates the row first; defensive only.

        var finishedAt = DateTime.UtcNow;
        state.LastFinishedAtUtc = finishedAt;
        state.LastDurationMs = (int)(finishedAt - startedAtUtc).TotalMilliseconds;
        state.LastSucceeded = succeeded;
        state.LastSummary = summary;
        state.LastError = error;
        await db.SaveChangesAsync(ct);
    }
}
