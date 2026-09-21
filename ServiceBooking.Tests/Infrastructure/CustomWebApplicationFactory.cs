using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Boots the real ServiceBooking.API pipeline (controllers, Identity, JWT, seeding)
/// against an isolated Postgres database ("servicebooking_test") instead of the dev DB.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Program.cs reads several config values (Jwt:Key, ConnectionStrings:DefaultConnection, ...)
        // as plain top-level statements BEFORE WebApplicationBuilder.Build() runs. ConfigureAppConfiguration
        // callbacks are merged in too late to affect those reads, so UseSetting is required here — it seeds
        // the configuration's initial in-memory source, which is visible from the very first read.
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

        // CYCLE5 (ARCHITECTURE_CYCLE5.md §48.1): HealthNoteProtector deliberately reuses
        // Notifications:EncryptionKey (cycle 4's channel-secret AES-GCM key) rather than a second key —
        // which means PUT .../health-note now 500s on THIS shared host too, not only the dedicated
        // NotificationTestFactory, the moment any test in the "Api" collection touches it. Same fixed,
        // deterministic 32-byte key NotificationTestFactory/NotificationDispatchTestFactory already use,
        // so a row encrypted under one factory type is readable by tests seeded via another.
        builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);

        // Quiet EF Core's per-query SQL logging — it drowns out `dotnet test` output otherwise.
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Warning");
    }
}
