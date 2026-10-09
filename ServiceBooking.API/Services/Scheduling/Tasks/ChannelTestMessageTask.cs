using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>ARCHITECTURE_CYCLE40.md §40.8 — sends the automatic check message to the owner after a number is bound (lane `realtime`, every 10 s).
/// Idempotent by the instance mark; see <see cref="ChannelTestMessageService"/>.</summary>
public sealed class ChannelTestMessageTask(ChannelTestMessageService service) : IScheduledTask
{
    public string Name => "channel-test-message";
    public TimeSpan DefaultPeriod => TimeSpan.FromSeconds(10);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var (scanned, sent) = await service.RunAsync(ct);
        return new ScheduledTaskOutcome(scanned, sent, 0, $"scanned {scanned}, sent {sent}");
    }
}
