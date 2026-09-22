using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA cycle 4 — a dedicated host for functional tests of the channel/connect/webhook/unsubscribe surface
/// (API_CONTRACT_CYCLE4.md §20-27, §32-33), by the same "behavior the shared Testing configuration
/// deliberately glues shut" pattern as <see cref="RateLimitTestFactory"/> (cycle 3) and
/// <see cref="NotificationDispatchTestFactory"/> (this cycle, ARCHITECTURE_CYCLE4.md §27). The plain
/// <see cref="CustomWebApplicationFactory"/> used by the shared "Api" collection leaves
/// <c>Notifications:EncryptionKey</c>/<c>WebhookToken</c>/<c>UnsubscribeKey</c> at their production-empty
/// defaults (appsettings.json), which makes <c>POST /connect</c> fail closed with 503 and both the
/// provider webhook and the unsubscribe link fail closed with 401/404 unconditionally — none of that
/// surface is reachable without a dedicated host that configures them, same reasoning as
/// <c>RateLimitTestFactory</c> existing instead of every test tightening the shared "Api" host's limits.
///
/// Deliberately does NOT enable <c>ScheduledTasks</c> (leaves Testing's own default, off) — tests that
/// need the real background runner ticking (priority/budget/idle) use
/// <see cref="NotificationDispatchTestFactory"/> instead; this one is for endpoints exercised directly
/// over HTTP. One instance per test (not shared), matching <see cref="RateLimitTestFactory"/>'s own
/// per-test-state reasoning — each test gets its own in-memory rate limiter / QR cache state.
/// </summary>
public sealed class NotificationTestFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>Same fixed, deterministic key <see cref="NotificationDispatchTestFactory"/> uses — lets a
    /// channel encrypted under one factory type be read by tests that happen to seed via the other.</summary>
    public const string TestEncryptionKeyBase64 = NotificationDispatchTestFactory.TestEncryptionKeyBase64;

    public const string TestWebhookToken = "qa-cycle4-webhook-token";
    public const string TestUnsubscribeKey = "qa-cycle4-unsubscribe-key-not-secret";

    /// <summary>This host's identity — see <see cref="TestHostSettings"/>. Populated once the host has
    /// started (e.g. by touching <see cref="WebApplicationFactory{TEntryPoint}.Services"/>).</summary>
    public TestHostIdentity Identity { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Identity = TestHostSettings.Apply(builder, TestSlot.Api, "ntf", connectionString);

        builder.UseSetting("Notifications:Provider", "logging"); // US-35 — never a real network call
        builder.UseSetting("Notifications:EncryptionKey", TestEncryptionKeyBase64);
        builder.UseSetting("Notifications:WebhookToken", TestWebhookToken);
        builder.UseSetting("Notifications:UnsubscribeKey", TestUnsubscribeKey);

        // CYCLE5 (ARCHITECTURE_CYCLE5.md §52.1, US-71): the production default leaves
        // InstanceCreationEnabled false and ServerCountry empty until ПЛ1 is confirmed with the provider
        // — POST /connect fail-closed 409s unconditionally at that default, same "behavior the shared
        // Testing configuration deliberately glues shut" reasoning as this class's own doc comment gives
        // for EncryptionKey/WebhookToken/UnsubscribeKey above. Provider transport stays "logging" (no
        // network call), so enabling instance creation here only unlocks the CONTROLLER'S OWN gate for
        // the pre-existing QR/connect surface this factory already exists to test.
        builder.UseSetting("Notifications:GreenApi:ServerCountry", "Russia");
        builder.UseSetting("Notifications:GreenApi:InstanceCreationEnabled", "true");
    }
}
