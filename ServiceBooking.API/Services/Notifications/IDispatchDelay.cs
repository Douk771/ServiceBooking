namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// The 5-15s pause between two sends on the same channel (ARCHITECTURE_CYCLE4.md §26.1) — an
/// asynchronous wait, never a blocked thread (§21 p.2). Abstracted so
/// <c>NotificationDispatchTestFactory</c>'s <c>RecordingDelay</c> can record every requested interval and
/// return immediately instead of a test actually waiting 5-15 real seconds per message (§27.1).
/// </summary>
public interface IDispatchDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>Production implementation: <see cref="Task.Delay(TimeSpan, CancellationToken)"/> — the
/// asynchronous wait the architecture requires, not <c>Thread.Sleep</c>.</summary>
public sealed class SystemDispatchDelay : IDispatchDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
