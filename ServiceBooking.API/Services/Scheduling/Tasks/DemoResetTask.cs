using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §580 (US-28-11) — the nightly reset of the demo: every 10 minutes it asks whether the local time of <c>DemoMode:TimeZoneId</c> has passed
/// <c>DemoMode:ResetLocalTime</c> since the last reset (<see cref="DemoResetSchedule"/>) and, if so, runs exactly the operator's <see cref="DemoResetService"/>.
/// Registered ONLY in demo mode (<c>AddBackgroundTasks</c>), so a production machine does not even list it. It never runs the first reset of a fresh database
/// (there is no stamp yet): that is the operator's <c>ops demo reset --yes</c>. A parallel operator run holds the advisory lock, and this task then skips.
/// </summary>
public sealed class DemoResetTask(
    AppDbContext db, IOptions<DemoModeOptions> options, DemoResetService reset, INotificationClock clock, ILogger<DemoResetTask> logger) : IScheduledTask
{
    public string Name => "demo-reset";
    public TimeSpan DefaultPeriod => TimeSpan.FromMinutes(10);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled) return new ScheduledTaskOutcome(0, 0, 0, "disabled");
        if (!settings.TryGetResetLocalTime(out var localTime))
            return new ScheduledTaskOutcome(0, 0, 0, "misconfigured") { Error = $"DemoMode:ResetLocalTime '{settings.ResetLocalTime}' is not HH:mm" };

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId); }
        catch (TimeZoneNotFoundException)
        {
            return new ScheduledTaskOutcome(0, 0, 0, "misconfigured") { Error = $"DemoMode:TimeZoneId '{settings.TimeZoneId}' is unknown" };
        }

        var now = clock.UtcNow;
        var last = await ReadLastResetAsync(ct);
        if (last is null) return new ScheduledTaskOutcome(1, 0, 0, "no reset yet — the first one is the operator's `ops demo reset --yes`");
        if (!DemoResetSchedule.IsDue(now, last, localTime, zone))
            return new ScheduledTaskOutcome(1, 0, 0, $"not due (last reset {last:yyyy-MM-dd HH:mm}Z)");

        DemoResetResult result;
        try
        {
            result = await reset.ResetAsync(now, confirmed: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The runner would record a cancelled task as a «partial success» (time budget reached), but for the reset that is a lie: the transaction is rolled
            // back, no reset was made, and the next tick would silently repeat it every 10 minutes. Make it an explicit error of the task.
            logger.LogError("demo-reset: cancelled (time budget or host shutdown) before it finished; the transaction is rolled back, the data were not reset");
            return new ScheduledTaskOutcome(1, 0, 0, "cancelled")
            {
                Error = "demo reset was cancelled (run-time budget or host shutdown) before it finished; nothing was reset — run `ops demo reset --yes` or raise the task's MaxRunMinutes",
            };
        }
        logger.LogInformation("demo-reset: exit {Exit}", result.ExitCode);
        var summary = string.Join(" | ", result.Lines.TakeLast(1));
        return result.ExitCode switch
        {
            DemoResetService.ExitOk => new ScheduledTaskOutcome(1, 1, 0, summary),
            DemoResetService.ExitLockBusy => new ScheduledTaskOutcome(1, 0, 0, "skipped: another showcase command is running"),
            _ => new ScheduledTaskOutcome(1, 0, 0, "failed") { Error = summary },
        };
    }

    private async Task<DateTime?> ReadLastResetAsync(CancellationToken ct)
    {
        var raw = await db.PlatformSettings.AsNoTracking().Where(s => s.Key == DemoCatalog.LastResetKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }
}
