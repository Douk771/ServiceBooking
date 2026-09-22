using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Boots the real ServiceBooking.API pipeline (controllers, Identity, JWT, seeding) against this run's
/// isolated "api" slot database (ARCHITECTURE_CYCLE8.md §66) instead of the dev DB.
/// </summary>
public class CustomWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>This host's identity (SuperAdmin credentials, file roots, ...) — see
    /// <see cref="TestHostSettings"/>. Populated by <see cref="ConfigureWebHost"/>, so only valid after
    /// the host has started (touching <see cref="WebApplicationFactory{TEntryPoint}.Services"/> is enough).</summary>
    public TestHostIdentity Identity { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Identity = TestHostSettings.Apply(builder, TestSlot.Api, "api", connectionString);

        // CYCLE5 (ARCHITECTURE_CYCLE5.md §48.1): HealthNoteProtector deliberately reuses
        // Notifications:EncryptionKey (cycle 4's channel-secret AES-GCM key) rather than a second key —
        // which means PUT .../health-note now 500s on THIS shared host too, not only the dedicated
        // NotificationTestFactory, the moment any test in the "Api" collection touches it. Same fixed,
        // deterministic 32-byte key NotificationTestFactory/NotificationDispatchTestFactory already use,
        // so a row encrypted under one factory type is readable by tests seeded via another.
        builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);
    }
}
