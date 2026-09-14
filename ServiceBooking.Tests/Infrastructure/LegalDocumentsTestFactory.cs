using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// A second, independent host pointed at a throw-away copy of App_Data/legal, so LEG- tests can rewrite
/// legal.json/*.html on disk mid-test (material vs. editorial version bumps, a live "swap the file, no
/// rebuild" replacement) without disturbing the documents every other functional test registers/logs in
/// against. ReloadSeconds is set to 1 so tests don't have to sleep 30s per version bump.
///
/// Shares the same Postgres database as the main "Api" collection (TestDatabaseFixture wipes it exactly
/// once for the whole assembly) — this factory never calls EnsureDeletedAsync itself, only boots its own
/// in-process TestServer against the already-migrated schema. Assembly-level
/// [CollectionBehavior(DisableTestParallelization = true)] guarantees no other collection's tests run
/// concurrently with this one, so the two hosts never race on the same rows.
/// </summary>
public sealed class LegalDocumentsTestFactory : WebApplicationFactory<Program>
{
    public string LegalRoot { get; }

    public LegalDocumentsTestFactory()
    {
        LegalRoot = Path.Combine(Path.GetTempPath(), "sb-legal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(LegalRoot);
        ResetToDefault();
    }

    /// <summary>Restores the manifest to two Material, draft documents with a fresh version tag (unique
    /// per call so tests don't collide with whatever a previous test in this run already accepted).</summary>
    public string ResetToDefault()
    {
        var version = "test-" + DateTime.UtcNow.Ticks + "-draft";
        WriteManifest(version, isDraft: true, changeKind: "Material");
        return version;
    }

    /// <summary>
    /// Writes a manifest where both documents share the given version/isDraft/changeKind. `version` must
    /// already end with "-draft" if isDraft is true — the provider rejects an isDraft document whose
    /// version doesn't carry the suffix (LegalDocumentProvider.LoadDocument), which is itself covered by
    /// LEG-014 below.
    /// </summary>
    public void WriteManifest(string version, bool isDraft, string changeKind, string? bodyMarker = null)
    {
        File.WriteAllText(Path.Combine(LegalRoot, "privacy.html"),
            $"<p>{(isDraft ? "<strong>Черновая редакция.</strong> " : "")}Политика {version}. {bodyMarker}</p>");
        File.WriteAllText(Path.Combine(LegalRoot, "terms.html"),
            $"<p>{(isDraft ? "<strong>Черновая редакция.</strong> " : "")}Соглашение {version}. {bodyMarker}</p>");

        File.WriteAllText(Path.Combine(LegalRoot, "legal.json"), $$"""
        {
          "documents": [
            { "type": "Privacy", "version": "{{version}}", "effectiveFrom": "2026-01-01", "isDraft": {{isDraft.ToString().ToLowerInvariant()}}, "changeKind": "{{changeKind}}", "title": "Политика обработки персональных данных", "file": "privacy.html" },
            { "type": "Terms",   "version": "{{version}}",   "effectiveFrom": "2026-01-01", "isDraft": {{isDraft.ToString().ToLowerInvariant()}}, "changeKind": "{{changeKind}}", "title": "Пользовательское соглашение", "file": "terms.html" }
          ]
        }
        """);

        // File.WriteAllText can land within the same mtime tick as the previous write on some
        // filesystems; the provider's cache key is purely mtime-based (LegalDocumentProvider.TryReload),
        // so back-to-back rewrites in the same test need a nudge to guarantee a new mtime is observed.
        File.SetLastWriteTimeUtc(Path.Combine(LegalRoot, "legal.json"), DateTime.UtcNow);
    }

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

        builder.UseSetting("Legal:Root", LegalRoot);
        builder.UseSetting("Legal:ReloadSeconds", "1");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(LegalRoot, recursive: true); } catch { /* best effort */ }
    }
}
