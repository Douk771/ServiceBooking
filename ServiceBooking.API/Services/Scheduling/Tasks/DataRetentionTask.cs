using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Retention;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.1). The fourth <see cref="IScheduledTask"/> — <see cref="ScheduledTaskRunner"/>
/// is unchanged for it, same as every task before it. Carries **no rule logic of its own**: it enumerates
/// whatever <see cref="IRetentionRule"/> was registered in <c>Program.cs</c> and runs each once, exactly the
/// contract §49.1 describes. There is, deliberately, no rule anywhere in this list (or registered in
/// <c>Program.cs</c>) that touches <see cref="Core.Entities.NotificationOptOut"/> — not a disabled one, not
/// one gated by a flag: the deletion code path for opt-outs does not exist in this codebase at all.
/// </summary>
public sealed class DataRetentionTask(
    IEnumerable<IRetentionRule> rules,
    IOptions<RetentionPeriods> periods,
    IConfiguration configuration,
    INotificationClock clock,
    ILogger<DataRetentionTask> logger) : IScheduledTask
{
    public string Name => "data-retention";
    public TimeSpan DefaultPeriod => TimeSpan.FromDays(1);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var section = configuration.GetSection($"ScheduledTasks:{Name}");
        // §49.2: dry-run is the DEFAULT — an operator has to opt in to the real thing by explicitly
        // setting this to false, never the other way round.
        var dryRun = section.GetValue("DryRun", true);
        var batchSize = section.GetValue("BatchSize", 500);

        // NowUtc comes from the SAME clock abstraction the dispatcher and channel-health task already use
        // (INotificationClock), read once here and handed to every rule as data (§49.4) — no rule below
        // ever reads DateTime.UtcNow itself. This is what lets a functional test move the fake clock
        // forward and see the effect immediately instead of waiting for real time to pass.
        var ctx = new RetentionContext(clock.UtcNow, periods.Value, batchSize, dryRun);
        var ruleList = rules.ToList();

        var scanned = 0;
        var affected = 0;
        var lines = new List<string>();

        // TD-04 (ARCHITECTURE_CYCLE16.md §246.1/§246.2): one rule throwing used to abort the whole
        // foreach — every later rule silently never ran, and the exception unwound past ExecuteAsync so
        // ScheduledTaskRunner lost LastSummary too (N9-6). Each rule now runs in its own try/catch, so a
        // failure is isolated to that one rule and the rest of the pass still happens.
        var okCount = 0;
        var skippedCount = 0;
        var failed = new List<string>();
        // Н6 (code review, cycle 18 3rd pass) — RetentionOutcome.RotationAffected was set by every rule
        // that passes isRotationRemoval but never read by anything: the per-rule count only ever
        // reached an operator buried inside that one rule's own Summary text. Aggregated here so the
        // task-level summary (the thing GET /api/admin/scheduled-tasks actually surfaces) answers "how
        // many of today's removals across ALL rules were a key-rotation forced removal, not an ordinary
        // age-based one" without an operator having to parse every per-rule sub-string themselves.
        var rotationAffected = 0;

        foreach (var rule in ruleList)
        {
            // The time budget / host shutdown must still propagate — only a rule's OWN exception is
            // isolated below.
            ct.ThrowIfCancellationRequested();

            try
            {
                var outcome = await rule.ApplyAsync(ctx, ct);
                scanned += outcome.Scanned;
                affected += outcome.Affected;
                rotationAffected += outcome.RotationAffected;
                lines.Add(outcome.Summary);
                if (outcome.Skipped) skippedCount++; else okCount++;
                // §49.2: one line per rule, numbers and dates only — never phone numbers, message text or
                // subject identifiers.
                logger.LogInformation("{Summary}", outcome.Summary);
            }
            catch (OperationCanceledException)
            {
                throw; // time budget / host shutdown — not a rule failure, must still unwind.
            }
            catch (Exception ex)
            {
                failed.Add(rule.Name);
                logger.LogError(ex, "retention rule {Rule} failed", rule.Name);
                lines.Add($"retention[{(dryRun ? "dry" : "live")}] {rule.Name}: FAILED ({ex.GetType().Name})");
            }
        }

        var summary = $"retention[{(dryRun ? "dry" : "live")}] ok={okCount} skipped={skippedCount} failed={failed.Count}" +
            (rotationAffected > 0 ? $" rotation-total={rotationAffected}" : string.Empty) +
            (lines.Count > 0 ? " | " + string.Join(" | ", lines) : string.Empty) +
            (failed.Count > 0 ? " | failed: " + string.Join(", ", failed) : string.Empty);

        // TD-04 (§246.3): a non-null Error means the task, as a whole, did not fully succeed — but the
        // summary above is still meaningful (it names exactly what ran, what was skipped and what
        // failed) and must reach GET /api/admin/scheduled-tasks rather than being wiped to null.
        var error = failed.Count > 0
            ? $"{failed.Count} of {ruleList.Count} retention rule(s) failed: {string.Join(", ", failed)}"
            : null;

        return new ScheduledTaskOutcome(scanned, affected, 0, summary) { Error = error };
    }
}
