using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE4.md §27 — a dedicated host with the REAL <c>ScheduledTaskRunner</c> actually
/// ticking, by the same pattern <c>RateLimitTestFactory</c> (cycle 3) already established for "behavior
/// the shared Testing configuration deliberately glues shut": <c>appsettings.Testing.json</c> sets
/// <c>ScheduledTasks:Enabled=false</c> precisely so the 400+ other functional tests sharing
/// <c>servicebooking_test</c> never race a background tick — which means the runner's OWN behavior
/// (advisory lock, budget, partial-pass handling) can only be exercised against a host that turns it back
/// on. One instance per test (not shared): each test gets its own <see cref="FakeClock"/>/recording state.
///
/// Three abstractions are swapped for recording/fake singletons registered AFTER Program.cs's own
/// (last registration wins for non-collection resolution) — <see cref="IDispatchDelay"/> →
/// <see cref="RecordingDelay"/>, <see cref="INotificationClock"/> → <see cref="FakeClock"/>,
/// <see cref="INotificationTransport"/> → <see cref="RecordingTransport"/>. A REAL network call never
/// happens through this host (US-35): even without this substitution, <c>Notifications:Provider=logging</c>
/// alone already guarantees that — the substitution exists to make sends OBSERVABLE and instant, not to
/// make them safe (they already were).
/// </summary>
public sealed class NotificationDispatchTestFactory(bool channelHealthEnabled = false) : WebApplicationFactory<Program>
{
    public RecordingDelay Delay { get; } = new();
    public FakeClock Clock { get; } = new();
    public RecordingTransport Transport { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestDatabaseFixture.ConnectionString);
        builder.UseSetting("Jwt:Key", "TEST_ONLY_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG");
        builder.UseSetting("Jwt:Issuer", "ServiceBooking");
        builder.UseSetting("Jwt:Audience", "ServiceBookingClient");
        builder.UseSetting("AllowedOrigins", "http://localhost:5173");
        builder.UseSetting("SuperAdmin:Phone", "+70000000001");
        builder.UseSetting("SuperAdmin:Email", "superadmin@test.local");
        builder.UseSetting("SuperAdmin:Password", "SuperAdmin123!");
        builder.UseSetting("SmartCaptcha:SecretKey", "");
        builder.UseSetting("SmartCaptcha:SiteKey", "");
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Warning");

        // §27.1: the runner ticks only under THIS host. TickSeconds=1 (not the 60s production default)
        // is what lets a test observe a second pass within real seconds instead of real minutes.
        builder.UseSetting("ScheduledTasks:Enabled", "true");
        builder.UseSetting("ScheduledTasks:TickSeconds", "1");

        // The only pre-existing task under this host, disabled so it never touches anything this test
        // didn't ask it to.
        builder.UseSetting("ScheduledTasks:photo-retention-cleanup:Enabled", "false");

        // §27.2: PeriodSeconds (not PeriodMinutes, whose smallest unit is a minute) — a fast, repeatable
        // dispatch pass.
        builder.UseSetting("ScheduledTasks:notification-dispatch:PeriodSeconds", "1");
        builder.UseSetting("ScheduledTasks:notification-dispatch:MaxRunMinutes", "1");
        builder.UseSetting("ScheduledTasks:channel-health:Enabled", channelHealthEnabled ? "true" : "false");
        builder.UseSetting("ScheduledTasks:channel-health:PeriodSeconds", "1");
        builder.UseSetting("ScheduledTasks:channel-health:MaxRunMinutes", "1");

        // Notifications:Enabled gates NotificationGate's tariff check (AllowNotificationChannel) only
        // through EffectivePlan, resolved from seeded DB state, not from this flag — but it is set so a
        // test seeding a company/channel end-to-end sees the same "notifications are a real feature"
        // configuration production would. Provider stays "logging" (US-35): no real GreenApi adapter is
        // EVER reachable from this host regardless of the substitutions below.
        builder.UseSetting("Notifications:Enabled", "true");
        builder.UseSetting("Notifications:Provider", "logging");
        // A valid, fixed 32-byte key so SecretProtector round-trips a seeded channel's ciphertext
        // (ChannelTestFactory.EncryptSecret uses the SAME literal) — not a real secret, this whole host
        // never leaves the test process.
        builder.UseSetting("Notifications:EncryptionKey", TestEncryptionKeyBase64);
        builder.UseSetting("Notifications:Dispatch:PauseMinMs", "5000");
        builder.UseSetting("Notifications:Dispatch:PauseMaxMs", "15000");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IDispatchDelay>(Delay);
            services.AddSingleton<INotificationClock>(Clock);
            services.AddSingleton<INotificationTransport>(Transport);
        });
    }

    /// <summary>32 zero bytes, base64-encoded — deterministic across every test process that uses this
    /// factory, so a channel seeded via <c>SecretProtector.Encrypt</c> with this same literal always
    /// decrypts inside the host built here.</summary>
    public const string TestEncryptionKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}

/// <summary>Records every interval <c>NotificationDispatchTask</c> asks to wait between two sends on the
/// same channel and returns immediately — the pause is observable without a test ever actually waiting
/// 5-15 real seconds per message (§27.1).</summary>
public sealed class RecordingDelay : IDispatchDelay
{
    private readonly ConcurrentQueue<TimeSpan> _requested = new();

    public IReadOnlyCollection<TimeSpan> Requested => _requested.ToArray();

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _requested.Enqueue(delay);
        return Task.CompletedTask;
    }
}

/// <summary>A clock a test moves by hand — starts at real <see cref="DateTime.UtcNow"/> so freshly-seeded
/// rows (CreatedAt, etc., still written with the real clock by EF/entity defaults) look "just created"
/// relative to it, then only moves when a test calls <see cref="Advance"/>.</summary>
public sealed class FakeClock : INotificationClock
{
    private DateTime _utcNow = DateTime.UtcNow;

    public DateTime UtcNow => _utcNow;

    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);

    public void Set(DateTime utcNow) => _utcNow = utcNow;
}

/// <summary>One recorded call to <see cref="INotificationTransport.SendAsync"/>, in the order it happened
/// — <see cref="RecordingTransport.Calls"/> is what a test asserts channel-interleaving/ordering against
/// (§27.3).</summary>
public sealed record RecordedSend(ChannelCredentials Credentials, string CanonicalPhone, string Text, DateTime AtUtc);

/// <summary>
/// Records every send in call order and returns a configurable outcome — default <see cref="SendOutcome.Sent"/>
/// with a synthetic id, exactly like <see cref="LoggingNotificationTransport"/>, but observable
/// (ARCHITECTURE_CYCLE4.md §27.1). Outcomes can be queued per-call (<see cref="EnqueueOutcome"/>) or
/// overridden per-phone (<see cref="SetOutcomeForPhone"/>) for tests that need a specific channel/message
/// to fail in a specific way (e.g. <c>ChannelInvalid</c> to prove a channel's group stops immediately).
/// </summary>
public sealed class RecordingTransport : INotificationTransport
{
    private readonly ConcurrentQueue<RecordedSend> _calls = new();
    private readonly ConcurrentQueue<SendOutcome> _queuedOutcomes = new();
    private readonly ConcurrentDictionary<string, SendOutcome> _outcomesByPhone = new();

    public IReadOnlyList<RecordedSend> Calls => _calls.ToArray();

    public void EnqueueOutcome(SendOutcome outcome) => _queuedOutcomes.Enqueue(outcome);

    public void SetOutcomeForPhone(string canonicalPhone, SendOutcome outcome) => _outcomesByPhone[canonicalPhone] = outcome;

    public Task<SendOutcome> SendAsync(ChannelCredentials credentials, string canonicalPhone, string text, CancellationToken ct)
    {
        _calls.Enqueue(new RecordedSend(credentials, canonicalPhone, text, DateTime.UtcNow));

        if (_outcomesByPhone.TryGetValue(canonicalPhone, out var byPhone))
            return Task.FromResult(byPhone);

        if (_queuedOutcomes.TryDequeue(out var queued))
            return Task.FromResult(queued);

        return Task.FromResult<SendOutcome>(new SendOutcome.Sent($"test-{Guid.NewGuid():N}"));
    }
}
