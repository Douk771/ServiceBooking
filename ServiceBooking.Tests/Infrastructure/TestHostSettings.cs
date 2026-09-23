using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using ServiceBooking.TestKit;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Single point of setup for every functional-test host — previously ~60 lines of
/// <c>builder.UseSetting(...)</c> copy-pasted across five factories (ARCHITECTURE_CYCLE8.md §71).
/// </summary>
public static class TestHostSettings
{
    private const string SuperAdminPassword = "SuperAdmin123!";

    /// <summary>Per-factoryTag SuperAdmin phone/email, per ARCHITECTURE_CYCLE8.md §71.1. The "api" and
    /// "legal" values are pinned to what they already were before cycle 8 — changing them would be a
    /// breaking, undocumented change to every test that logs in as SuperAdmin today.</summary>
    private static readonly IReadOnlyDictionary<string, (string Phone, string Email)> SuperAdmins =
        new Dictionary<string, (string, string)>
        {
            ["api"] = ("+70000000001", "superadmin@test.local"),
            ["ratelimit"] = ("+70000000002", "superadmin-ratelimit@test.local"),
            ["ntf"] = ("+70000000003", "superadmin-ntf@test.local"),
            ["uploads"] = ("+70000000004", "superadmin-uploads@test.local"),
            ["dispatch"] = ("+70000000005", "superadmin-dispatch@test.local"),
            ["legal"] = ("+70000099999", "superadmin-legal-isolated@test.local"),
            // QA cycle 9 (ARCHITECTURE_CYCLE9.md §104.2, B13) — a host that is deliberately never meant
            // to finish starting (bad Notifications:Provider value), see NotificationTransportStartupTests.
            ["startup-qa9"] = ("+70000000006", "superadmin-startup-qa9@test.local"),
        };

    /// <param name="builder">the host being configured</param>
    /// <param name="factoryTag">short unique tag for the factory type — see <see cref="SuperAdmins"/> for
    /// the closed set of valid values</param>
    /// <param name="connectionString">the class database's connection string, from
    /// <see cref="TestDatabaseFixture"/></param>
    public static TestHostIdentity Apply(IWebHostBuilder builder, string factoryTag, string connectionString)
    {
        if (!SuperAdmins.TryGetValue(factoryTag, out var admin))
        {
            throw new ArgumentOutOfRangeException(nameof(factoryTag), factoryTag,
                "Unknown factoryTag — add its SuperAdmin phone/email to TestHostSettings.SuperAdmins " +
                "(ARCHITECTURE_CYCLE8.md §71.1) before using it.");
        }

        var databaseName = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database
            ?? throw new ArgumentException("connectionString has no Database segment", nameof(connectionString));

        // ARCHITECTURE_CYCLE8_PHASE2.md §91.5/§99 (T8-P4): file roots are scoped to the CLASS slot, not
        // factoryTag — before this, every one of the 27 "Api"-collection classes shared factoryTag "api"
        // and therefore one temp directory, so 25+ concurrently-parallelized classes would clash on
        // File.Copy while copying App_Data/legal into it (T8-P1 recon's IOException). The class slot is
        // the database name's own last segment (sbtest_<runkey>_<slot>, TestDatabaseNaming's own scheme),
        // read back out here rather than threaded through six factory constructors as a separate
        // parameter — every host in one test class already shares one database, so this stays scoped to
        // the class by construction. Scoping to the class slot instead of factoryTag means concurrent
        // (different) classes never touch the same directory, while multiple hosts booted WITHIN one
        // class (e.g. a fresh NotificationDispatchTestFactory per test) still correctly share one root,
        // matching their shared, sequential-by-construction test class.
        var classSlot = databaseName.Split('_', 3) is [_, _, var slot] ? slot : databaseName;
        var runRoot = Path.Combine(Path.GetTempPath(), "sb-test", TestRunKey.Current, classSlot);
        var publicRoot = Path.Combine(runRoot, "public");
        var privateRoot = Path.Combine(runRoot, "private");
        var stateRoot = Path.Combine(runRoot, "state");
        var logDirectory = Path.Combine(runRoot, "logs");
        var legalRoot = Path.Combine(runRoot, "legal");

        Directory.CreateDirectory(publicRoot);
        Directory.CreateDirectory(privateRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(logDirectory);
        CopyLegalManifest(legalRoot);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Jwt:Key", "TEST_ONLY_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG");
        builder.UseSetting("Jwt:Issuer", "ServiceBooking");
        builder.UseSetting("Jwt:Audience", "ServiceBookingClient");
        builder.UseSetting("AllowedOrigins", "http://localhost:5173");
        builder.UseSetting("SuperAdmin:Phone", admin.Phone);
        builder.UseSetting("SuperAdmin:Email", admin.Email);
        builder.UseSetting("SuperAdmin:Password", SuperAdminPassword);
        builder.UseSetting("SmartCaptcha:SecretKey", "");
        builder.UseSetting("SmartCaptcha:SiteKey", "");
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Warning");

        // §71.3: file resources move from repo-relative shared directories to a run+factory-scoped temp
        // root, so no two hosts (even within the same slot) ever write over each other's uploads/state.
        builder.UseSetting("Storage:PublicRoot", publicRoot);
        builder.UseSetting("Storage:PrivateRoot", privateRoot);
        builder.UseSetting("Legal:Root", legalRoot);

        // §71.4: logs move out of the process' working directory.
        builder.UseSetting("Logs:Directory", logDirectory);

        return new TestHostIdentity(
            connectionString, admin.Phone, admin.Email, SuperAdminPassword,
            publicRoot, privateRoot, stateRoot, logDirectory, legalRoot,
            classSlot, databaseName);
    }

    /// <summary>Copies App_Data/legal into <paramref name="destination"/> — every factory gets its own
    /// copy so US-87 ("result doesn't depend on which collection started first") holds even for factories
    /// that never rewrite the manifest themselves (§71.3).</summary>
    private static void CopyLegalManifest(string destination)
    {
        Directory.CreateDirectory(destination);
        var repoLegalRoot = FindRepoLegalRoot();
        foreach (var file in Directory.GetFiles(repoLegalRoot))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        PublishTermsOwner(Path.Combine(destination, "legal.json"));
    }

    /// <summary>Cycle 11 (ARCHITECTURE_CYCLE11.md §102.7/§102.10, T8): the real
    /// App_Data/legal/legal.json now ships with every document — including TermsOwner, the channel-offer
    /// carrier — marked <c>isDraft: true</c>, since a live lawyer hasn't proofread the new set yet, and
    /// TermsOwner's own HTML still carries unresolved <c>{{PLACEHOLDER}}</c> tokens (operator's legal
    /// name/INN/OGRN/address — filled in once real company details exist). That's correct for the
    /// product, but PricingCatalogCache.IsCatalogPubliclyVisibleAsync gates the whole public pricing
    /// catalog on TermsOwner being published, so every pre-existing pricing/billing test that never cared
    /// about the legal gate would otherwise start seeing 404 on <c>GET /api/pricing</c> for a reason that
    /// has nothing to do with what those tests exercise. LegalDocumentProvider.LoadDocument itself refuses
    /// to publish a document with unresolved placeholders (ARCHITECTURE_CYCLE5.md §43.3) — correctly so —
    /// which means simply flipping isDraft without also resolving the placeholders makes the manifest fail
    /// to load at all (every test hitting a NullReferenceException on a missing snapshot).
    ///
    /// So both are patched here, in the per-factory COPY (never the committed App_Data/legal artifact —
    /// that file is built by ServiceBooking.LegalKit and hand-editing it would just make the next
    /// `legalkit check` / CI run flag drift): unresolved placeholders get stand-in fixture values, then
    /// isDraft flips to false and the "-draft" version suffix is dropped. Every factory built through
    /// TestHostSettings.Apply gets a stable, published TermsOwner by default. LegalDocumentsTestFactory is
    /// unaffected: it overwrites Legal:Root with its own generated manifest (no placeholders in it) right
    /// after calling Apply (see its ConfigureWebHost), and LegalPricingGateTests' own WriteManifest already
    /// controls TermsOwner's draft state directly for the tests whose whole point is exercising this
    /// gate.</summary>
    private static void PublishTermsOwner(string legalJsonPath)
    {
        var json = JsonNode.Parse(File.ReadAllText(legalJsonPath))!.AsObject();
        var documents = json["documents"]!.AsArray();
        var termsOwner = documents.Single(d => (string?)d!["type"] == "TermsOwner")!.AsObject();

        var fileName = (string)termsOwner["file"]!;
        var filePath = Path.Combine(Path.GetDirectoryName(legalJsonPath)!, fileName);
        var html = File.ReadAllText(filePath);
        html = PlaceholderRegex().Replace(html, "тестовое значение");
        File.WriteAllText(filePath, html);

        termsOwner["isDraft"] = false;
        var version = (string)termsOwner["version"]!;
        if (version.EndsWith("-draft", StringComparison.Ordinal))
            termsOwner["version"] = version[..^"-draft".Length];

        File.WriteAllText(legalJsonPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // The shared pattern, not a second copy of it — LegalDocumentProvider.PlaceholderPattern is public
    // specifically so nothing else in the repo hand-rolls its own placeholder regex (ARCHITECTURE_CYCLE11.md
    // §106.3): a fixture with its own copy is exactly the drift that guarantee exists to prevent.
    private static readonly System.Text.RegularExpressions.Regex PlaceholderRegexInstance =
        new(ServiceBooking.API.Services.Legal.LegalDocumentProvider.PlaceholderPattern);

    private static System.Text.RegularExpressions.Regex PlaceholderRegex() => PlaceholderRegexInstance;

    private static string FindRepoLegalRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ServiceBooking.API", "App_Data", "legal");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate ServiceBooking.API/App_Data/legal by walking up from AppContext.BaseDirectory " +
            $"({AppContext.BaseDirectory}) — has the repository layout changed?");
    }
}

/// <summary>Everything a test host's identity consists of under cycle 8's isolation scheme — the return
/// value of <see cref="TestHostSettings.Apply"/>.</summary>
public sealed record TestHostIdentity(
    string ConnectionString,
    string SuperAdminPhone,
    string SuperAdminEmail,
    string SuperAdminPassword,
    string PublicRoot,
    string PrivateRoot,
    string StateRoot,
    string LogDirectory,
    string LegalRoot,
    string ClassSlot,
    string DatabaseName);
