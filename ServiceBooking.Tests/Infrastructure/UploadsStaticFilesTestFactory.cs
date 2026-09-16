using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// A dedicated, non-shared host (mirrors <see cref="RateLimitTestFactory"/>'s pattern) that can override
/// <c>Storage:PublicRoot</c> and/or the host's own content root — the two knobs the cycle-3
/// Program.cs bugfix (serve <c>/uploads</c> from <see cref="ServiceBooking.API.Services.FileStorage"/>'s
/// resolved public root, not the parameterless <c>app.UseStaticFiles()</c>'s hard-wired <c>wwwroot</c>)
/// actually depends on.
///
/// Deliberately NOT the shared "Api" collection's <see cref="CustomWebApplicationFactory"/>: that host is
/// fixed to the repo's own content root with no <c>Storage:PublicRoot</c> override, which is exactly the
/// configuration under which the parameterless <c>app.UseStaticFiles()</c> and the fixed version behave
/// identically (both resolve to the same <c>wwwroot/uploads</c>) — it could never distinguish the two, so
/// PROF-016/PROF-017 only caught the regression by the accident of <c>wwwroot</c> not being committed to
/// git (see UPL-001/UPL-002 in ServiceBooking.Tests/Tests/UploadsStaticFilesTests.cs for the two scenarios
/// that close that gap for real).
/// </summary>
public sealed class UploadsStaticFilesTestFactory(string? publicRootOverride = null, string? contentRootOverride = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        if (contentRootOverride is not null)
        {
            builder.UseContentRoot(contentRootOverride);

            // Registration goes through LegalConsentFilter, which needs a real legal.json manifest
            // (ARCHITECTURE.md §4). LegalDocumentProvider defaults to <content root>/App_Data/legal,
            // which doesn't exist under a deliberately-empty content root override — point it back at
            // the repo's real manifest via Legal:Root so this factory can still register a user. This
            // is orthogonal to what these tests actually pin (Storage:PublicRoot / wwwroot resolution);
            // it only exists to keep an unrelated dependency out of the way.
            var repoLegalRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "ServiceBooking.API", "App_Data", "legal");
            builder.UseSetting("Legal:Root", Path.GetFullPath(repoLegalRoot));
        }

        // Same minimal settings CustomWebApplicationFactory seeds — see its own comment on why
        // UseSetting (not ConfigureAppConfiguration) is required for values Program.cs reads before
        // WebApplicationBuilder.Build(). Needed here independently of contentRootOverride: when the
        // content root is overridden to an empty temp directory, appsettings.Testing.json isn't found
        // there at all, so nothing but these explicit settings (plus GetValue(...) code defaults for
        // everything else, e.g. Uploads:MaxFileBytes/PerUserPerMinute) is available.
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

        if (publicRootOverride is not null)
            builder.UseSetting("Storage:PublicRoot", publicRootOverride);
    }
}
