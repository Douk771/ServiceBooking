using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.Scheduling;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA cycle 9 (ARCHITECTURE_CYCLE9.md §105.8, task C12) — the Web Push mirror of
/// <see cref="NotificationDispatchTestFactory"/>: a dedicated host with the REAL
/// <c>ScheduledTaskRunner</c> ticking <c>staff-push-dispatch</c> (glued off by default in
/// <c>appsettings.Testing.json</c>, same reasoning as <c>notification-dispatch</c>/<c>channel-health</c>).
/// <c>Notifications:StaffPush:Provider</c> is set to <c>"web-push"</c> so <c>POST /api/push/subscriptions</c>
/// and <c>GET /api/push/config</c> behave as "enabled" — but <see cref="IWebPushSender"/> is swapped for
/// <see cref="FakeWebPushSender"/> (registered AFTER Program.cs's own, so it wins at resolution): no real
/// network call ever happens through this host, matching every other dispatch-style factory in this
/// suite (US-124's own "ни одного сетевого вызова наружу" requirement for the test contour, §105.3).
///
/// Cycle 14 (flaky-CI fix): <paramref name="disableAutomaticTicking"/> lets a test opt OUT of the real
/// <c>PeriodicTimer</c>/<c>ScheduledTaskRunner</c> wall-clock tick entirely and drive one dispatch pass
/// deterministically via <see cref="RunStaffPushDispatchPassAsync"/> instead. Tests that only assert a
/// TERMINAL decision reached by <c>ProcessRowAsync</c> (re-checking rights/subscription ownership from the
/// DB) rather than the runner's own scheduling behavior (its advisory lock, its due-time bookkeeping, its
/// budget/partial-pass handling) lose nothing by skipping the timer: <c>StaffPushDispatchTask.ExecuteAsync</c>
/// is the exact same production code either way, just invoked directly in its own DI scope instead of by
/// the runner's <c>workScope</c> — see <see cref="RunStaffPushDispatchPassAsync"/> for why the advisory
/// lock and <c>ScheduledTaskState</c> bookkeeping the runner ALSO does around that call are immaterial to
/// those tests (nothing else runs the task concurrently in a single-pass test, and nothing asserts
/// <c>ScheduledTaskState</c>). Cycle 14, второй заход: 410/429 (PUSH-006/PUSH-007) тоже переведены сюда —
/// «нет повтора после 410» доказывается ДВУМЯ явными проходами строже, чем подсчётом отправок за окно
/// ожидания, где число тиков зависело от загрузки машины. На реальном таймере в этом классе больше не
/// ждёт никто.
/// </summary>
public sealed class PushDispatchTestFactory(string connectionString, bool disableAutomaticTicking = false) : WebApplicationFactory<Program>
{
    public FakeClock Clock { get; } = new();
    public FakeWebPushSender Sender { get; } = new();

    public TestHostIdentity Identity { get; private set; } = null!;

    public const string TestEncryptionKeyBase64 = NotificationDispatchTestFactory.TestEncryptionKeyBase64;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Identity = TestHostSettings.Apply(builder, "dispatch", connectionString);

        // disableAutomaticTicking: the runner's own BackgroundService.ExecuteAsync returns immediately
        // when ScheduledTasks:Enabled=false (see ScheduledTaskRunner.cs) — the task itself stays fully
        // registered and configured below, so RunStaffPushDispatchPassAsync can still resolve and run it.
        builder.UseSetting("ScheduledTasks:Enabled", disableAutomaticTicking ? "false" : "true");
        builder.UseSetting("ScheduledTasks:TickSeconds", "1");
        builder.UseSetting("ScheduledTasks:photo-retention-cleanup:Enabled", "false");
        builder.UseSetting("ScheduledTasks:notification-dispatch:Enabled", "false");
        builder.UseSetting("ScheduledTasks:channel-health:Enabled", "false");
        builder.UseSetting("ScheduledTasks:staff-push-dispatch:Enabled", "true");
        builder.UseSetting("ScheduledTasks:staff-push-dispatch:PeriodSeconds", "1");
        builder.UseSetting("ScheduledTasks:staff-push-dispatch:MaxRunMinutes", "1");

        builder.UseSetting("Notifications:Provider", "logging"); // WhatsApp/MAX stay untouched (§100.3)
        builder.UseSetting("Notifications:EncryptionKey", TestEncryptionKeyBase64);

        // §105.3: Testing counts as a "developer environment" (DeploymentSafetyChecks.IsDeveloperEnvironment)
        // — Provider=web-push is allowed to start here with NO real VAPID keys at all, because the only
        // thing that reads them (LibWebPushSender) is never resolved: the override below always wins.
        builder.UseSetting("Notifications:StaffPush:Provider", "web-push");
        builder.UseSetting("Notifications:StaffPush:Dispatch:BudgetSeconds", "20");
        builder.UseSetting("Notifications:StaffPush:Dispatch:MaxParallelEndpoints", "8");
        builder.UseSetting("Notifications:StaffPush:Dispatch:MaxAttempts", "4");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<INotificationClock>(Clock);
            services.AddSingleton<IWebPushSender>(Sender);
        });
    }

    /// <summary>
    /// Runs exactly one <c>staff-push-dispatch</c> pass synchronously, in its own fresh DI scope — the
    /// same call <c>ScheduledTaskRunner.RunOneAsync</c> makes (<c>workScope.ServiceProvider
    /// .GetServices&lt;IScheduledTask&gt;().First(t => t.Name == task.Name).ExecuteAsync(...)</c>), just
    /// without waiting for a real <see cref="PeriodicTimer"/> tick to get there. Deliberately does NOT
    /// reproduce the runner's advisory lock or <c>ScheduledTaskState</c> due-time bookkeeping: those exist
    /// to keep MULTIPLE overlapping runner instances from double-processing the same row, which cannot
    /// happen from a single direct call a test makes itself — skipping them changes nothing about what
    /// <c>ProcessRowAsync</c> decides for a given row. Requires <c>disableAutomaticTicking: true</c> so no
    /// real tick can race this call and process the very row the test is about to seed/mutate.
    /// </summary>
    public async Task<ScheduledTaskOutcome> RunStaffPushDispatchPassAsync()
    {
        using var scope = Services.CreateScope();
        var task = scope.ServiceProvider.GetServices<IScheduledTask>().First(t => t.Name == "staff-push-dispatch");
        return await task.ExecuteAsync(CancellationToken.None);
    }
}

/// <summary>One recorded call to <see cref="IWebPushSender.SendAsync"/>.</summary>
public sealed record RecordedPushSend(Guid SubscriptionId, string Endpoint, string PayloadJson, int TtlSeconds, string Topic);

/// <summary>
/// Records every push send and returns a configurable outcome — default <see cref="WebPushSendOutcome.Sent"/>,
/// matching <see cref="ServiceBooking.API.Services.Notifications.WebPush.LoggingWebPushSender"/>'s own
/// "accept everything, never touch the network" shape, but OBSERVABLE (mirrors
/// <see cref="RecordingTransport"/>'s own doc comment for the WhatsApp/MAX side).
/// </summary>
public sealed class FakeWebPushSender : IWebPushSender
{
    private readonly ConcurrentQueue<RecordedPushSend> _calls = new();
    private readonly ConcurrentDictionary<Guid, Queue<WebPushSendOutcome>> _outcomesBySubscription = new();

    public IReadOnlyList<RecordedPushSend> Calls => _calls.ToArray();

    /// <summary>Queues one outcome for the NEXT call(s) to this specific subscription id, consumed in
    /// FIFO order — scoped per-subscription (not a single global FIFO) for the same reason
    /// <see cref="RecordingTransport.SetOutcomeForPhone"/> is: this suite's real dispatcher scans
    /// Pending rows PLATFORM-WIDE, so a global queue could be consumed by an unrelated leftover row.</summary>
    public void EnqueueOutcomeFor(Guid subscriptionId, WebPushSendOutcome outcome) =>
        _outcomesBySubscription.GetOrAdd(subscriptionId, _ => new Queue<WebPushSendOutcome>()).Enqueue(outcome);

    public Task<WebPushSendOutcome> SendAsync(
        WebPushSubscriptionTarget target, string payloadJson, int ttlSeconds, string topic, CancellationToken ct)
    {
        _calls.Enqueue(new RecordedPushSend(target.SubscriptionId, target.Endpoint, payloadJson, ttlSeconds, topic));

        if (_outcomesBySubscription.TryGetValue(target.SubscriptionId, out var queue) && queue.TryDequeue(out var outcome))
            return Task.FromResult(outcome);

        return Task.FromResult<WebPushSendOutcome>(new WebPushSendOutcome.Sent());
    }
}
