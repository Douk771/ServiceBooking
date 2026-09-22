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

        var scanned = 0;
        var affected = 0;
        var lines = new List<string>();

        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();

            var outcome = await rule.ApplyAsync(ctx, ct);
            scanned += outcome.Scanned;
            affected += outcome.Affected;
            lines.Add(outcome.Summary);
            // §49.2: one line per rule, numbers and dates only — never phone numbers, message text or
            // subject identifiers.
            logger.LogInformation("{Summary}", outcome.Summary);
        }

        var summary = string.Join(" | ", lines);
        return new ScheduledTaskOutcome(scanned, affected, 0, summary);
    }
}
