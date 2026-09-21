namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.1: "последнее — не БД, а проверка возраста файлов в logs/,
/// только предупреждение в сводку"). Deliberately never deletes a file — log rotation/retention is the
/// deployment's job (DEPLOY.md), not this task's; this rule is a compliance TRIPWIRE that surfaces how
/// many files are older than <see cref="RetentionPeriods.AppLogDays"/> so a misconfigured or disabled
/// log-rotation policy is visible in <c>ScheduledTaskState.LastSummary</c> instead of silently
/// accumulating IP addresses and phone numbers (LEGAL_REVIEW.md §13.5) forever. <see cref="RetentionContext.DryRun"/>
/// has no effect here — there is nothing this rule ever writes, dry-run or not.
/// </summary>
public sealed class AppLogAgeRule : IRetentionRule
{
    public string Name => "app-log";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        // Reads AppLogDirectory/AppLogDays off ctx.Periods, same as every other rule — no separate
        // constructor dependency needed. An earlier version took a plain RetentionPeriods constructor
        // parameter, which DI can never satisfy on its own (only IOptions<RetentionPeriods> is
        // registered, Program.cs's Configure<RetentionPeriods> call) — a resolution failure that only
        // surfaced on the full functional run, once something actually asked for IEnumerable<IRetentionRule>,
        // not on `dotnet build` or the unit suite. Fixed by removing the dependency entirely instead of
        // adding an IOptions<T> wrapper nothing else needed.
        var periods = ctx.Periods;
        var directory = periods.AppLogDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[n/a] {Name}: no log directory configured"));

        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).AppLog;
        var scanned = 0;
        var stale = 0;
        DateTime? oldest = null;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            DateTime writeTimeUtc;
            try { writeTimeUtc = File.GetLastWriteTimeUtc(file); }
            catch (IOException) { continue; } // vanished mid-scan (log rotation) — not this rule's problem

            if (writeTimeUtc >= cutoff) continue;
            stale++;
            if (oldest is null || writeTimeUtc < oldest) oldest = writeTimeUtc;
        }

        var summary = stale == 0
            ? $"retention[warn] {Name}: scanned={scanned} affected=0"
            : $"retention[warn] {Name}: scanned={scanned} affected={stale} oldest={oldest:yyyy-MM-dd} " +
              $"(files older than {periods.AppLogDays}d found — not deleted, check log rotation)";
        return Task.FromResult(new RetentionOutcome(Name, scanned, stale, summary));
    }
}
