using ServiceBooking.API.Services.Stays;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>ARCHITECTURE_CYCLE37.md §37.7.3 — turns expired holds into "Снята: не оплачена" in batches of 50 (lane `realtime`, every 15 s). One transaction per booking under its house lock; the conditional UPDATE of the race (§37.5.3) decides against a proof upload.</summary>
public sealed class StaysHoldExpiryTask(StayHoldExpirer expirer, ServiceOrderHoldExpirer orderExpirer, ILogger<StaysHoldExpiryTask> logger) : IScheduledTask
{
    public const int BatchSize = 50;
    public string Name => "stays-hold-expiry";
    public TimeSpan DefaultPeriod => TimeSpan.FromSeconds(15);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var expired = await expirer.ExpireDueAsync(BatchSize, ct);
        // The second pass (ARCHITECTURE_CYCLE39.md §39.7.7): stand-alone orders of services, the same conditional UPDATE and the same race.
        expired += await orderExpirer.ExpireDueAsync(BatchSize, ct);
        if (expired > 0) logger.LogInformation("stays-hold-expiry: expired {Count} unpaid hold(s)", expired);
        return new ScheduledTaskOutcome(expired, expired, 0, $"expired {expired}");
    }
}
