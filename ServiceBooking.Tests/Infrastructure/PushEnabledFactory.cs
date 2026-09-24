using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA cycle 9 (task C12) — a secondary host against the SAME class database an <see cref="ApiTestBase"/>
/// subclass already has, existing for exactly one reason: the shared "Api" collection factory
/// (<see cref="CustomWebApplicationFactory"/>) leaves <c>Notifications:StaffPush:Provider</c> at its
/// Testing default (<c>"logging"</c>, <c>appsettings.Testing.json</c>) — under which
/// <c>POST /api/push/subscriptions</c>/<c>GET /api/push/config</c> correctly answer "not enabled" (409),
/// same "behavior the shared Testing configuration deliberately glues shut" situation
/// <see cref="RateLimitTestFactory"/> already exists for. One instance per test (not shared), same
/// reasoning as <see cref="RateLimitTestFactory"/>/<see cref="NotificationTestFactory"/>: booted against
/// the SAME connection string as the test class' own <see cref="ApiTestBase.Factory"/>, so a booking/
/// company/master created through the ordinary shared host is visible here too — only the push-specific
/// endpoints are called through this one.
/// </summary>
public sealed class PushEnabledFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestHostSettings.Apply(builder, "api", connectionString);
        builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);
        builder.UseSetting("Notifications:StaffPush:Provider", "web-push");
    }
}
