using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// ARCHITECTURE_CYCLE35.md §35.10.3 (A35-5) — "demo-board-tick" (every 2 minutes, lane "main", <c>MaxRunMinutes = 1</c>): the live board of the demo shops. The whole
/// decision is in <see cref="DemoBoardTicker"/>; this class is the plug. Registered ONLY in demo mode (<c>AddBackgroundTasks</c>), like <see cref="DemoResetTask"/>, so a
/// production machine does not even list it; the service checks both locks of the demo again.
/// </summary>
public sealed class DemoBoardTickTask(DemoBoardTicker ticker, INotificationClock clock) : IScheduledTask
{
    public string Name => "demo-board-tick";
    public TimeSpan DefaultPeriod => TimeSpan.FromMinutes(2);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var result = await ticker.TickAsync(clock.UtcNow, ct);
        return new ScheduledTaskOutcome(result.Scanned, result.Advanced + result.Closed, 0, result.Summary);
    }
}
