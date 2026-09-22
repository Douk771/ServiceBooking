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
        };

    /// <param name="builder">the host being configured</param>
    /// <param name="slot">which slot's database this host talks to (<see cref="TestSlot"/>)</param>
    /// <param name="factoryTag">short unique tag for the factory type — see <see cref="SuperAdmins"/> for
    /// the closed set of valid values</param>
    /// <param name="connectionString">the slot's database connection string, from
    /// <see cref="TestRunEnvironment"/></param>
    public static TestHostIdentity Apply(IWebHostBuilder builder, string slot, string factoryTag, string connectionString)
    {
        if (!SuperAdmins.TryGetValue(factoryTag, out var admin))
        {
            throw new ArgumentOutOfRangeException(nameof(factoryTag), factoryTag,
                "Unknown factoryTag — add its SuperAdmin phone/email to TestHostSettings.SuperAdmins " +
                "(ARCHITECTURE_CYCLE8.md §71.1) before using it.");
        }

        var runRoot = Path.Combine(Path.GetTempPath(), "sb-test", TestRunKey.Current, factoryTag);
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
            publicRoot, privateRoot, stateRoot, logDirectory, legalRoot);
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
    }

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
    string LegalRoot);
