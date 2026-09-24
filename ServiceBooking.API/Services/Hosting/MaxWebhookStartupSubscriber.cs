using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.API.Services.Hosting;

/// <summary>
/// One-shot subscribe attempt at process start (ARCHITECTURE_CYCLE12.md §146.3, Q4) — registered in
/// Program.cs ONLY when <c>PhoneVerification:Provider = "max-bot"</c>. Deliberately an
/// <see cref="IHostedService"/>, not a seventh <c>IScheduledTask</c>: the scheduled-task runner decides
/// "is it time yet" from <c>ScheduledTaskState</c> persisted in the database, which survives a restart —
/// a renewal that last ran an hour ago on a 4-hour period would correctly NOT re-run immediately after a
/// restart, but the spec requires exactly that (a fresh process must re-subscribe on its own start,
/// regardless of when the periodic task last ran). <c>MaxWebhookRenewTask</c> is the periodic half of
/// this same requirement — both exist, neither replaces the other.
/// </summary>
public sealed class MaxWebhookStartupSubscriber(MaxWebhookSubscriber subscriber) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        // Awaited by the host, so startup pauses for up to Max:TimeoutSeconds (default 5s, the same
        // bound MaxBotClient's HttpClient enforces on the subscribe call itself) — bounded and
        // acceptable, the same shape as the fail-fast checks that already run before Build(). What
        // §146.3 actually requires ("старт не роняет") is that a subscribe FAILURE never aborts startup
        // — guaranteed here because SubscribeAsync itself never throws (see its own doc comment).
        subscriber.SubscribeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
