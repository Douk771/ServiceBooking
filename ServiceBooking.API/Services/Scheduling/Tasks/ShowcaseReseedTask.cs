using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.7 (US-28-07, P1) — re-seeds the showcase once a week so that its dates keep moving with the calendar. Registered always; while
/// <c>Showcase:Reseed:Enabled</c> is off (the default) it returns at once with the summary "disabled". It never CREATES a showcase (no showcase — nothing to
/// re-seed) and uses exactly the operator's <c>recreate</c> (one transaction under the advisory lock <c>ops:showcase</c>, so a parallel operator run makes it skip).
/// The time of the last re-seed is kept in <c>PlatformSettings</c> <c>showcase.last-reseed-utc</c>, stamped by every create/recreate.
/// </summary>
public sealed class ShowcaseReseedTask(
    AppDbContext db, IOptions<ShowcaseReseedOptions> options, ShowcaseCommands commands, INotificationClock clock,
    ILogger<ShowcaseReseedTask> logger) : IScheduledTask
{
    public string Name => "showcase-reseed";
    public TimeSpan DefaultPeriod => TimeSpan.FromHours(1);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled) return new ScheduledTaskOutcome(0, 0, 0, "disabled");
        if (!settings.TryGetLocalTime(out var localTime))
            return new ScheduledTaskOutcome(0, 0, 0, "misconfigured") { Error = $"Showcase:Reseed:LocalTime '{settings.LocalTime}' is not HH:mm" };

        var now = clock.UtcNow;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        if (!await db.Companies.AnyAsync(c => c.IsShowcase, ct))
            return new ScheduledTaskOutcome(0, 0, 0, "no showcase — nothing to re-seed");

        var last = await ReadLastReseedAsync(ct);
        if (!ShowcaseReseedSchedule.IsDue(now, last, settings.DayOfWeek, localTime, zone))
            return new ScheduledTaskOutcome(1, 0, 0, $"not due (last re-seed {last:yyyy-MM-dd HH:mm}Z)");

        var result = await commands.RunAsync(OpsAction.ShowcaseRecreate, planOf: null, confirmed: true, now, ct);
        logger.LogInformation("showcase-reseed: exit {Exit}", result.ExitCode);
        return result.ExitCode switch
        {
            ShowcaseCommands.ExitOk => new ScheduledTaskOutcome(1, 1, 0, string.Join(" | ", result.Lines.TakeLast(1))),
            ShowcaseCommands.ExitLockBusy => new ScheduledTaskOutcome(1, 0, 0, "skipped: another showcase command is running"),
            _ => new ScheduledTaskOutcome(1, 0, 0, "refused") { Error = string.Join(" | ", result.Lines.TakeLast(1)) },
        };
    }

    private async Task<DateTime?> ReadLastReseedAsync(CancellationToken ct)
    {
        var raw = await db.PlatformSettings.AsNoTracking().Where(s => s.Key == ShowcaseCatalog.LastReseedKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }
}
