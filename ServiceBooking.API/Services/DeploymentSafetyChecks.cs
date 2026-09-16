using Microsoft.Extensions.Configuration;

namespace ServiceBooking.API.Services;

/// <summary>
/// Fail-fast checks for "obviously unsafe deployment configuration" outside Development/Testing
/// (US-10 → US-48, ARCHITECTURE.md §13). Pulled out of Program.cs's top-level statements into pure,
/// DI-free static methods that take only <see cref="IConfiguration"/> and a couple of plain strings —
/// not <c>IWebHostEnvironment</c>/<c>IServiceProvider</c> — specifically so ServiceBooking.UnitTests can
/// call them directly against an in-memory <see cref="IConfigurationRoot"/> with no host, no HTTP
/// pipeline and no database (US-48 test-coverage gap, QA cycle C: this logic previously had zero
/// automated coverage — reading Program.cs was the only way to know it worked). Program.cs remains the
/// only production caller, at exactly the two points these checks always ran; behavior is unchanged,
/// only where the logic lives.
/// </summary>
public static class DeploymentSafetyChecks
{
    /// <summary>
    /// Allow-list, not deny-list (US-48, cycle C): an environment nobody told this code about yet
    /// (Staging, Preview, Demo, ...) must be treated as production-grade and get the full set of checks.
    /// Only the two environments known to be developer contexts are exempt. Case-insensitive, matching
    /// <c>IHostEnvironment.IsDevelopment()</c>/<c>IsEnvironment(...)</c>'s own comparison.
    /// </summary>
    public static bool IsDeveloperEnvironment(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Jwt:Key, SuperAdmin:Password/Phone and Storage:PrivateRoot — everything knowable from
    /// configuration alone, before <c>WebApplicationBuilder.Build()</c> runs. Throws
    /// <see cref="InvalidOperationException"/> for the two secrets and the storage path (a misconfigured
    /// deployment must never finish starting); SuperAdmin:Phone is a warning only, via <paramref
    /// name="warn"/> — it isn't a secret the way the password/JWT key are, so a deployment that forgot to
    /// override it stays reachable, just with a foreseeable login (see Program.cs's original comment).
    /// </summary>
    /// <param name="configuration">Configuration to validate.</param>
    /// <param name="contentRootPath">App content root, used to resolve Storage:PrivateRoot's and
    /// Storage:PublicRoot's defaults and to compute wwwroot's absolute path for the containment checks.</param>
    /// <param name="warn">Sink for the non-fatal SuperAdmin:Phone warning. Defaults to
    /// <see cref="Console.WriteLine(string?)"/>, matching Program.cs; tests supply their own to assert on
    /// it without touching stdout.</param>
    public static void ValidateSecrets(IConfiguration configuration, string contentRootPath, Action<string>? warn = null)
    {
        warn ??= Console.WriteLine;

        var jwtKeyValue = configuration["Jwt:Key"];
        if (string.IsNullOrEmpty(jwtKeyValue) || jwtKeyValue.Length < 32 ||
            jwtKeyValue == "CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS")
            throw new InvalidOperationException(
                "Jwt:Key is missing, too short (<32 chars) or still the placeholder. Set Jwt__Key in .env.");

        // Two placeholders reach this check, not one: "Admin12345" ships in appsettings.json, and
        // "CHANGE_ME" ships in .env.production.example. The second one is the more dangerous of the two —
        // it passes a naive placeholder check but fails the Identity password policy (no digit, no
        // lowercase), so the seed step would fail to create the account and the operator would see an
        // obscure downstream error instead of this message.
        var superAdminPassword = configuration["SuperAdmin:Password"];
        if (string.IsNullOrEmpty(superAdminPassword) || superAdminPassword is "Admin12345" or "CHANGE_ME")
            throw new InvalidOperationException(
                "SuperAdmin:Password is missing or still a placeholder. Set SuperAdmin__Password in .env " +
                "to a real password (at least 8 characters, with a digit, an uppercase and a lowercase letter).");

        if (configuration["SuperAdmin:Phone"] == "+70000000000")
            warn("WARNING: SuperAdmin:Phone is still the placeholder +70000000000. Set SuperAdmin__Phone in .env.");

        // US-19 p.4 / ARCHITECTURE.md §3.4: a private root that resolves inside wwwroot would be served
        // to anyone with the link by UseStaticFiles — the one realistic way client photos leak by
        // accident (risk R2) is a typo'd .env, so this must stop the deployment, not just log a warning.
        // Calls FileStorage's own default-resolution helper (rather than resolving it through the DI
        // container, which isn't built yet at the point Program.cs calls this) so the two can never drift
        // apart (sanitation cycle, review round 2: this used to duplicate the logic inline).
        var privateRootFull = Path.GetFullPath(FileStorage.ResolvePrivateRoot(configuration, contentRootPath));
        var wwwrootFull = Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot")) + Path.DirectorySeparatorChar;
        if (privateRootFull.StartsWith(wwwrootFull, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Storage:PrivateRoot resolves inside wwwroot — client photos would be served by " +
                "UseStaticFiles to anyone with the link. Set Storage__PrivateRoot to a path outside wwwroot.");

        // Checked ahead of the Storage:PrivateRoot-vs-Storage:PublicRoot comparison below on purpose
        // (reordered during the sanitation cycle's second review pass): on the shipped Production
        // configuration (Storage__PublicRoot=/app, Storage__PrivateRoot=/app/private-uploads), the
        // private-vs-public check below would fire FIRST and tell the operator to move
        // Storage__PrivateRoot — the wrong knob, since the actual mistake is Storage__PublicRoot
        // swallowing the app's own content root. A public root that swallows the content root is the more
        // fundamental error (it leaks the app itself, not just client photos) and must be reported first,
        // even when both problems are present at once. Before the sanitation cycle, UseStaticFiles was
        // hard-wired to wwwroot, so Storage:PublicRoot couldn't widen what got served no matter what it
        // was set to. Now Program.cs builds its PhysicalFileProvider directly over
        // FileStorage.PublicRootFullPath (see Program.cs's comment at the UseStaticFiles call), so a
        // public root pointed at or above the app's own content root — a typo, or a well-meaning "make
        // uploads work" edit — turns /uploads/... into a listing of the application itself:
        // appsettings.Production.json, the compiled DLLs, App_Data/legal/... . Guard against that the same
        // way the private-root checks below do: the served directory must not be the content root, and
        // must not be an ancestor of it.
        var publicRootFull = Path.GetFullPath(FileStorage.ResolvePublicRoot(configuration, contentRootPath))
            .TrimEnd(Path.DirectorySeparatorChar);
        var contentRootFull = Path.GetFullPath(contentRootPath).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(publicRootFull, contentRootFull, StringComparison.Ordinal) ||
            contentRootFull.StartsWith(publicRootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Storage:PublicRoot resolves to the application's content root or an ancestor of it — " +
                "UseStaticFiles would serve the app's own files (appsettings, DLLs, App_Data) at " +
                "/uploads/... to anyone. Set Storage__PublicRoot to a dedicated uploads directory, not the " +
                "app folder or anything above it.");

        // The check above is about the DEFAULT public location; since the sanitation cycle the directory
        // UseStaticFiles actually exposes is Storage:PublicRoot (Program.cs builds its PhysicalFileProvider
        // over FileStorage.PublicRootFullPath), which only equals wwwroot/uploads when left unset. Pointing
        // the public root somewhere custom and the private root inside THAT would leak client photos while
        // the wwwroot comparison above stayed silent — so the served directory has to be compared too, not
        // just the default one. Equality is checked separately from containment: both roots being the very
        // same directory is the worst case of all, and a StartsWith(root + separator) test alone does not
        // catch it.
        var privateRootTrimmed = privateRootFull.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(privateRootTrimmed, publicRootFull, StringComparison.Ordinal) ||
            privateRootTrimmed.StartsWith(publicRootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Storage:PrivateRoot resolves inside Storage:PublicRoot — client photos would be served by " +
                "UseStaticFiles at /uploads/... to anyone with the link. Set Storage__PrivateRoot to a path " +
                "outside the public uploads root.");
    }

    /// <summary>
    /// ForwardedHeaders:TrustedNetworks must be non-empty outside Development/Testing. An empty list
    /// doesn't make ForwardedHeadersMiddleware "ignore" X-Forwarded-For (ARCHITECTURE.md §9.1) — it makes
    /// it trust the header UNCONDITIONALLY from any peer (<c>KnownNetworks.Count == 0 &amp;&amp;
    /// KnownProxies.Count == 0</c> disables the check entirely rather than failing it), which lets any
    /// caller spoof the address the rate limiter partitions on — a denial-of-service footgun, not a
    /// limiter (confirmed against a bare TestServer probe, QA cycle C / SEC-042).
    /// </summary>
    public static void ValidateTrustedNetworksConfigured(IConfiguration configuration)
    {
        var trustedNetworks = configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>();
        if (trustedNetworks is null || trustedNetworks.Length == 0)
            throw new InvalidOperationException(
                "ForwardedHeaders:TrustedNetworks is empty — the rate limiter would partition every caller " +
                "under nginx's own address instead of the real client IP, which is a denial-of-service " +
                "footgun, not a limiter. Set FORWARDEDHEADERS__TRUSTEDNETWORKS__0 in .env (the docker bridge " +
                "subnet — see DEPLOY.md).");
    }
}
