using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.GreenApi;
using ServiceBooking.API.Services.Notifications.WebPush;

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
        // (reordered during the sanitation cycle's second review pass): on a MISTYPED Production
        // configuration — Storage__PublicRoot=/app set by hand over the shipped
        // Storage__PrivateRoot=/app/private-uploads; the stack itself never sets a public root, see
        // .env.production.example — the
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

    /// <summary>
    /// Cycle 4, US-54/US-35 (ARCHITECTURE_CYCLE4.md §24.2). Two independent rules, gated differently on
    /// purpose:
    ///
    /// 1–2. When notifications are enabled and this is not a developer environment, the encryption key
    ///    and (if the provider requires one, per NotificationOptions.PartnerToken) the partner token
    ///    must be present and not placeholders —
    ///    a misconfigured Production deployment must never finish starting with notifications silently
    ///    unusable.
    /// 3. Outside Production — including Development and Testing, unlike rules 1–2 — the provider
    ///    partner token must be EMPTY. This is a mirror-image safety rule: a real partner token on a
    ///    developer's machine can create or delete a live salon's WhatsApp instance, which is exactly the
    ///    kind of accident a "skip checks in Development" exemption must not enable.
    /// </summary>
    public static void ValidateNotificationSecrets(IConfiguration configuration, string environmentName)
    {
        // Matches NotificationOptions.Provider's own default — an absent key must read the same way here
        // as it does everywhere else that binds this section, not as "not logging" by accident.
        var provider = configuration["Notifications:Provider"] ?? "logging";
        // I4: gated on the PROVIDER, not Notifications:Enabled — Enabled ships false in
        // appsettings.json and, since T4-B10, does nothing else at all (it stopped being read by
        // NotificationGate/the scheduled tasks; see the cycle report). Gating fail-fast on a flag that no
        // longer gates anything meant a Production box with a real provider configured started up completely
        // unchecked — no encryption key, no partner token, no key-fingerprint protection — as long as
        // nobody had also flipped Enabled=true. "logging" is the one provider that can never reach a real
        // WhatsApp account or need a real secret, so it is the one value exempt from these checks.
        var isRealProvider = !string.Equals(provider, "logging", StringComparison.OrdinalIgnoreCase);
        var isDeveloperEnvironment = IsDeveloperEnvironment(environmentName);
        var isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

        if (isRealProvider && !isDeveloperEnvironment)
        {
            ValidateEncryptionKeyFormat(configuration["Notifications:EncryptionKey"]);

            if (string.Equals(provider, GreenApiProviderName.Value, StringComparison.OrdinalIgnoreCase))
            {
                var partnerToken = configuration["Notifications:PartnerToken"];
                if (string.IsNullOrWhiteSpace(partnerToken) || partnerToken == "CHANGE_ME")
                    throw new InvalidOperationException(
                        $"Notifications:Provider is '{GreenApiProviderName.Value}' but Notifications:PartnerToken is missing " +
                        "or still a placeholder. Set NOTIFICATIONS_PARTNER_TOKEN in .env.");

                // ARCHITECTURE_CYCLE9.md §104.1/§104.9: GREEN-API's MAX product is a SEPARATE partner
                // account from WhatsApp (confirmed by the provider's own docs during B1 — createInstance's
                // response typeInstance is tied to which partner token called it, not a request
                // parameter), so it needs its OWN partner token, checked the same way and for the same
                // reason as WhatsApp's above — a real provider configured with no way to provision MAX
                // instances must fail loud at startup, not 503 the first owner who requests a MAX channel.
                var maxPartnerToken = configuration["Notifications:GreenApiMax:PartnerToken"];
                if (string.IsNullOrWhiteSpace(maxPartnerToken) || maxPartnerToken == "CHANGE_ME")
                    throw new InvalidOperationException(
                        $"Notifications:Provider is '{GreenApiProviderName.Value}' but Notifications:GreenApiMax:PartnerToken " +
                        "is missing or still a placeholder — MAX is a separate GREEN-API product/account and needs its own " +
                        "partner token, distinct from Notifications:PartnerToken. Set NOTIFICATIONS_GREENAPI_MAX_PARTNER_TOKEN in .env.");
            }

            // I4: an empty UnsubscribeKey doesn't fail loudly anywhere downstream — NotificationScheduler
            // just silently omits the mandatory opt-out line (US-33, US-59 п. 3) from every message it
            // renders, which is a legal-compliance problem, not a crash, and would otherwise ship
            // unnoticed until someone reads message bodies by hand.
            if (string.IsNullOrWhiteSpace(configuration["Notifications:UnsubscribeKey"]))
                throw new InvalidOperationException(
                    "Notifications:UnsubscribeKey is missing while a real notification provider is " +
                    "configured — every outgoing message would ship without the mandatory unsubscribe " +
                    "line (US-33/US-59 п. 3). Set NOTIFICATIONS_UNSUBSCRIBE_KEY in .env.");

            // I4: an empty WebhookToken doesn't crash either — ProviderWebhook just 401s every call
            // forever (ConstantTimeEquals against an empty expected token never matches), so delivery
            // status and channel-state pushes silently never arrive.
            if (string.IsNullOrWhiteSpace(configuration["Notifications:WebhookToken"]))
                throw new InvalidOperationException(
                    "Notifications:WebhookToken is missing while a real notification provider is " +
                    "configured — the provider webhook would 401 every call forever. Set " +
                    "NOTIFICATIONS_WEBHOOK_TOKEN in .env.");
        }

        if (!isProduction)
        {
            var partnerToken = configuration["Notifications:PartnerToken"];
            if (!string.IsNullOrWhiteSpace(partnerToken))
                throw new InvalidOperationException(
                    "Notifications:PartnerToken is set outside Production. A real provider partner token " +
                    "here could create or delete a live salon's WhatsApp instance from a dev/test run. " +
                    "Clear NOTIFICATIONS_PARTNER_TOKEN outside Production.");

            var maxPartnerToken = configuration["Notifications:GreenApiMax:PartnerToken"];
            if (!string.IsNullOrWhiteSpace(maxPartnerToken))
                throw new InvalidOperationException(
                    "Notifications:GreenApiMax:PartnerToken is set outside Production — the same mirror-image " +
                    "rule as Notifications:PartnerToken (ARCHITECTURE_CYCLE9.md §104.1). Clear " +
                    "NOTIFICATIONS_GREENAPI_MAX_PARTNER_TOKEN outside Production.");
        }
    }

    /// <summary>
    /// T-24 (ARCHITECTURE_CYCLE5.md §52.3): <c>Notifications:ProviderDeliveryConsent</c> must be one of
    /// <c>Strict</c>/<c>AccountsOnly</c>/<c>Off</c> (case-insensitive) — an unrecognized value fails loud
    /// at startup, the same convention <c>Notifications:Provider</c> already follows (an operator typo
    /// here would otherwise silently fall back to whichever branch <c>Enum.Parse</c> happens to default
    /// to, and this value governs a legal gate, not a cosmetic setting). Runs in every environment,
    /// unconditionally — unlike most of this class's checks, there is no "safe in Development" carve-out:
    /// a misconfigured value is just as wrong on a laptop as in Production, and the whole point of this
    /// flag being config (not code) is that it is cheap to get right everywhere.
    /// </summary>
    public static void ValidateProviderDeliveryConsentMode(IConfiguration configuration)
    {
        var raw = configuration["Notifications:ProviderDeliveryConsent"];
        if (string.IsNullOrWhiteSpace(raw)) return; // absent → NotificationOptions' own default (AccountsOnly)

        if (!Enum.TryParse<Core.Enums.ProviderDeliveryConsentMode>(raw, ignoreCase: true, out _))
            throw new InvalidOperationException(
                $"Notifications:ProviderDeliveryConsent is '{raw}', which is not one of Strict/AccountsOnly/Off. " +
                "Fix the configured value — see ARCHITECTURE_CYCLE5.md §52.3/§52.4 for what each means and costs.");
    }

    /// <summary>
    /// T5-B13 (ARCHITECTURE_CYCLE5.md §52.1, US-71 п. 6, ч. 5 ст. 18 152-ФЗ): if instance creation is
    /// turned on, the server country MUST be set — "the provider decides" is not an acceptable default
    /// for a data-localization requirement. Runs unconditionally, same reasoning as
    /// <see cref="ValidateProviderDeliveryConsentMode"/>: this is a config-consistency check, not an
    /// environment-gated secret check, so there is no "safe in Development" carve-out for it either.
    /// </summary>
    public static void ValidateGreenApiServerCountry(IConfiguration configuration)
    {
        var creationEnabled = configuration.GetValue<bool>("Notifications:GreenApi:InstanceCreationEnabled");
        if (!creationEnabled) return;

        var serverCountry = configuration["Notifications:GreenApi:ServerCountry"];
        if (string.IsNullOrWhiteSpace(serverCountry))
            throw new InvalidOperationException(
                "Notifications:GreenApi:InstanceCreationEnabled is true but Notifications:GreenApi:ServerCountry " +
                "is empty. Set NOTIFICATIONS_GREEN_API_SERVER_COUNTRY in .env — creating real WhatsApp instances " +
                "without a known server location risks violating ч. 5 ст. 18 152-ФЗ (data localization).");
    }

    /// <summary>
    /// T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.1, §49.5). Two minimums are legally load-bearing, not just
    /// defaults an operator is free to shorten, so — same reasoning as
    /// <see cref="ValidateProviderDeliveryConsentMode"/> and <see cref="ValidateGreenApiServerCountry"/> —
    /// this runs unconditionally, in every environment, with no "safe in Development" carve-out:
    /// <c>Retention:TemplateHistoryDays</c> must be at least 365 (advertising limitation period, ст. 4.5
    /// КоАП) and <c>Retention:ConsentRecordDays</c> must be at least 1095 (general limitation period,
    /// ст. 196 ГК — the operator must be able to PROVE consent, ч. 1 ст. 9). "Не меньше трёх лет" must not
    /// depend on who last edited appsettings.Production.json.
    /// </summary>
    public static void ValidateRetentionPeriods(IConfiguration configuration)
    {
        var section = configuration.GetSection(ServiceBooking.API.Services.Retention.RetentionPeriods.SectionName);

        var templateHistoryDays = section.GetValue<int?>("TemplateHistoryDays")
                                   ?? new ServiceBooking.API.Services.Retention.RetentionPeriods().TemplateHistoryDays;
        if (templateHistoryDays < 365)
            throw new InvalidOperationException(
                $"Retention:TemplateHistoryDays is {templateHistoryDays}, below the 365-day minimum " +
                "(1-year advertising limitation period, ст. 4.5 КоАП — LEGAL_REVIEW.md §13.5). Set " +
                "RETENTION__TEMPLATEHISTORYDAYS to at least 365.");

        var consentRecordDays = section.GetValue<int?>("ConsentRecordDays")
                                 ?? new ServiceBooking.API.Services.Retention.RetentionPeriods().ConsentRecordDays;
        if (consentRecordDays < 1095)
            throw new InvalidOperationException(
                $"Retention:ConsentRecordDays is {consentRecordDays}, below the 1095-day (3-year) minimum " +
                "(general limitation period, ст. 196 ГК — the operator must be able to prove consent, " +
                "ч. 1 ст. 9, LEGAL_REVIEW.md §13.5). Set RETENTION__CONSENTRECORDDAYS to at least 1095.");
    }

    private static void ValidateEncryptionKeyFormat(string? keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64) || keyBase64 == "CHANGE_ME")
            throw new InvalidOperationException(
                "Notifications:EncryptionKey is missing or still a placeholder while notifications are " +
                "enabled. Set NOTIFICATIONS_ENCRYPTION_KEY in .env to a base64-encoded 32-byte key " +
                "(openssl rand -base64 32).");

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Notifications:EncryptionKey is not valid base64. Set NOTIFICATIONS_ENCRYPTION_KEY in " +
                ".env to a base64-encoded 32-byte key (openssl rand -base64 32).");
        }

        if (keyBytes.Length != 32)
            throw new InvalidOperationException(
                $"Notifications:EncryptionKey must decode to exactly 32 bytes, got {keyBytes.Length}. Set " +
                "NOTIFICATIONS_ENCRYPTION_KEY in .env to a base64-encoded 32-byte key (openssl rand -base64 32).");
    }

    /// <summary>
    /// Cycle 4, US-54 (ARCHITECTURE_CYCLE4.md §24.5, risk R10). Fail-fast if the key this process was
    /// started with does not match the fingerprint recorded on a previous start, unless the mismatch was
    /// acknowledged as a deliberate rotation — see <see cref="ChannelKeyFingerprint"/> for the decision
    /// table. Gated the same way as <see cref="ValidateNotificationSecrets"/>'s rules 1–2 (enabled AND
    /// not a developer environment): if notifications are off, there is nothing to protect yet; on a
    /// developer machine, the key is expected to churn freely. Must be called AFTER
    /// <see cref="ValidateNotificationSecrets"/> — it assumes the key already passed the format check.
    /// </summary>
    /// <param name="configuration">Configuration to validate.</param>
    /// <param name="environmentName">Current <c>ASPNETCORE_ENVIRONMENT</c>.</param>
    /// <param name="contentRootPath">App content root, used to resolve <c>Notifications:KeyFingerprintPath</c>'s default.</param>
    /// <param name="warn">Sink for the non-fatal rotation-acknowledgement warning (§24.4/§24.5's
    /// decision table, last row). Reviewer note: previously hardcoded to <see cref="Console.WriteLine(string?)"/>,
    /// which never reaches Serilog/GlitchTip at all — same fix as <see cref="ValidateSecrets"/>'s own
    /// <paramref name="warn"/> parameter, for the same reason. Defaults to <see cref="Console.WriteLine(string?)"/>
    /// only so existing callers that don't pass one keep working; Program.cs passes a Serilog-backed sink.</param>
    public static void ValidateChannelKeyFingerprint(
        IConfiguration configuration, string environmentName, string contentRootPath, Action<string>? warn = null)
    {
        warn ??= Console.WriteLine;

        // I4: same gating fix as ValidateNotificationSecrets — Provider, not the no-longer-load-bearing
        // Notifications:Enabled flag.
        var provider = configuration["Notifications:Provider"] ?? "logging";
        var isRealProvider = !string.Equals(provider, "logging", StringComparison.OrdinalIgnoreCase);
        if (!isRealProvider || IsDeveloperEnvironment(environmentName)) return;

        var keyBytes = SecretProtector.DecodeKey(configuration["Notifications:EncryptionKey"]);
        var rotationAck = configuration["Notifications:KeyRotationAck"];

        var configuredPath = configuration["Notifications:KeyFingerprintPath"];
        var relativePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine("App_Data", "state", ".notifications-key-fingerprint")
            : configuredPath;
        var fullPath = Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(contentRootPath, relativePath);

        ChannelKeyFingerprint.ValidateAndPersist(
            keyBytes,
            rotationAck,
            fullPath,
            File.Exists,
            File.ReadAllText,
            (path, content) =>
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, content);
            },
            warn);
    }

    /// <summary>
    /// Cycle 4, US-30 (ARCHITECTURE_CYCLE4.md §34.3). Resolves the least-common IANA zone the cycle
    /// depends on — <c>Asia/Barnaul</c>, not <c>Europe/Moscow</c>, deliberately: a widely-used zone can
    /// be present in a stripped-down tzdata image while a less common one is missing, so checking the
    /// common one would pass on exactly the image that fails a company in Barnaul. .NET 6+ resolves IANA
    /// ids from the OS time zone database on Linux (no <c>TimeZoneConverter</c> package needed), so this
    /// is really a check that the runtime image installed <c>tzdata</c> at all.
    /// </summary>
    public static void ValidateTimeZoneDatabase(string environmentName)
    {
        if (IsDeveloperEnvironment(environmentName)) return;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Barnaul");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                "The 'Asia/Barnaul' IANA time zone could not be resolved — the OS time zone database " +
                "(tzdata) is missing or incomplete in this image. Companies whose city resolves to this " +
                "zone would get wrong visit/reminder times. Install tzdata in the runtime stage of the " +
                "Dockerfile.", ex);
        }
    }

    /// <summary>
    /// Parses <c>Booking:DefaultWorkWindow</c> (ARCHITECTURE_CYCLE6.md §46.2) — the fallback window
    /// staff get on a date with no schedule row and <c>manual=true</c> but no <c>extendedHours</c>.
    /// Pure and DI-free like the rest of this class, so it's testable without a host. An unparsable or
    /// inverted value must fail the deployment loudly (CURRENT_STATE.md §6 convention: never fall back
    /// silently to a whole day) rather than surface as "the grid looks wrong" days later.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) ParseDefaultWorkWindow(IConfiguration configuration)
    {
        var startRaw = configuration["Booking:DefaultWorkWindow:Start"];
        var endRaw = configuration["Booking:DefaultWorkWindow:End"];

        if (string.IsNullOrWhiteSpace(startRaw) || string.IsNullOrWhiteSpace(endRaw))
            throw new InvalidOperationException(
                "Booking:DefaultWorkWindow:Start/End are missing. Set both in appsettings.json.");

        if (!TimeOnly.TryParse(startRaw, out var start) || !TimeOnly.TryParse(endRaw, out var end))
            throw new InvalidOperationException(
                $"Booking:DefaultWorkWindow:Start/End could not be parsed as times (\"{startRaw}\"/\"{endRaw}\").");

        if (start >= end)
            throw new InvalidOperationException(
                $"Booking:DefaultWorkWindow:Start ({start}) must be before End ({end}).");

        return (start, end);
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE9.md §105.3 (US-124, Q15, R12): mirrors <see cref="ValidateNotificationSecrets"/>'s
    /// shape for the Web Push subsystem's OWN, separate secret (VAPID, not the channel master key).
    /// <c>Notifications:StaffPush:Provider = "logging"</c> (the default) needs nothing — that's the
    /// documented "невыпущенность" state (SPEC П13), not a misconfiguration. <c>"web-push"</c> outside a
    /// developer environment REQUIRES a public/private key pair that actually parses as P-256
    /// (<see cref="VapidKeyValidator.IsValidP256Pair"/>) and a non-empty Subject — "старт падает", not
    /// "работает наполовину". Any other value fails loud, same append-only-provider convention as
    /// <see cref="NotificationOptions.Provider"/>.
    /// </summary>
    public static void ValidateStaffPushSecrets(IConfiguration configuration, string environmentName)
    {
        var provider = configuration[$"{WebPushOptions.SectionName}:Provider"] ?? "logging";
        if (string.Equals(provider, "logging", StringComparison.OrdinalIgnoreCase)) return;

        if (!string.Equals(provider, "web-push", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Notifications:StaffPush:Provider is '{provider}', which is neither 'logging' nor 'web-push'. " +
                "Fix the configured value — see ARCHITECTURE_CYCLE9.md §105.3.");

        if (IsDeveloperEnvironment(environmentName)) return;

        var publicKey = configuration[$"{WebPushOptions.SectionName}:VapidPublicKey"];
        var privateKey = configuration[$"{WebPushOptions.SectionName}:VapidPrivateKey"];
        if (!VapidKeyValidator.IsValidP256Pair(publicKey, privateKey))
            throw new InvalidOperationException(
                "Notifications:StaffPush:Provider is 'web-push' but VapidPublicKey/VapidPrivateKey are " +
                "missing or do not parse as a P-256 key pair. Generate a pair (e.g. `npx web-push " +
                "generate-vapid-keys`) and set WEBPUSH_VAPID_PUBLIC_KEY/WEBPUSH_VAPID_PRIVATE_KEY in .env " +
                "— see ARCHITECTURE_CYCLE9.md §105.2/§105.3.");

        if (string.IsNullOrWhiteSpace(configuration[$"{WebPushOptions.SectionName}:VapidSubject"]))
            throw new InvalidOperationException(
                "Notifications:StaffPush:Provider is 'web-push' but VapidSubject is empty. Set " +
                "WEBPUSH_VAPID_SUBJECT in .env to a mailto: or https: URL identifying the platform " +
                "(RFC 8292) — see ARCHITECTURE_CYCLE9.md §105.2.");

        // B5/§105.3: PushSubscriptionWriter.UpsertAsync encrypts p256dh/auth with the SAME
        // Notifications:EncryptionKey ValidateNotificationSecrets already guards — but only when
        // Notifications:Provider itself is a real provider. A deployment can run
        // Notifications:Provider=logging (no WhatsApp/MAX configured) with
        // Notifications:StaffPush:Provider=web-push at the same time; that combination started up clean
        // and then 500'd on the very first POST /api/push/subscriptions (SecretProtector.DecodeKey throws
        // on an empty/placeholder key). "web-push" needs this key exactly as much as a real
        // Notifications:Provider does — checked here too, last (after the keys this method already owns),
        // so the failure is at startup, not first request.
        ValidateEncryptionKeyFormat(configuration["Notifications:EncryptionKey"]);
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE9.md §104.2 (US-122): "нераспознанное значение по-прежнему роняет старт; вдобавок
    /// роняет старт ситуация «в реестре нет реализации для члена NotificationTransport»." Unlike this
    /// class's other checks, the registry itself is built from DI in <c>Program.cs</c> (it depends on
    /// which concrete adapters got wired up), so this method takes the registry's OWN answer to "what did
    /// you actually end up with" — <paramref name="registeredTransports"/> — as plain data instead of
    /// re-deriving it, keeping the check itself pure/DI-free like the rest of this class and callable
    /// directly from a unit test with a hand-built list.
    /// </summary>
    /// <param name="registeredTransports">What the registry actually ended up with — every member of
    /// <see cref="Core.Enums.NotificationTransport"/> missing from this collection fails the check.</param>
    /// <param name="registryName">Which registry this is, for the exception message —
    /// <see cref="Notifications.INotificationTransportRegistry"/> and
    /// <see cref="Notifications.IChannelProvisioningRegistry"/> are checked separately, since one could in
    /// principle be complete while the other isn't (e.g. Production intentionally omits provisioning for
    /// a transport it can still SEND through).</param>
    public static void ValidateTransportRegistryCompleteness(string registryName, IReadOnlyCollection<Core.Enums.NotificationTransport> registeredTransports)
    {
        var missing = Enum.GetValues<Core.Enums.NotificationTransport>().Except(registeredTransports).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"{registryName} has no implementation registered for: {string.Join(", ", missing)}. " +
                "Every NotificationTransport member must have an adapter wired up in Program.cs before the " +
                "app finishes starting — a transport with no adapter must fail loud at startup, not silently " +
                "\"just not send\" the first time a message for it comes due (ARCHITECTURE_CYCLE9.md §104.2).");
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE13.md §206/§209.2 (LEGAL_REVIEW.md §16.2). Mirrors
    /// <see cref="ValidateNotificationSecrets"/>'s shape (own section, own Provider switch, unrecognized
    /// value ALWAYS fails startup) with one addition that is NOT environment-gated at all:
    /// <c>AddressVerification:CacheHours</c> outside [0, 720] fails startup in EVERY environment,
    /// including Development/Testing — 720 hours (30 days) is the standard Yandex Geocoder licence's own
    /// ceiling on "temporary caching for performance" (LEGAL_REVIEW.md §16.2/§16.5), a legal fact about
    /// the licence, not a tunable that is only risky in Production. Do not move this check inside an
    /// <c>if (!isDeveloperEnvironment)</c> guard — a developer testing a higher cache value locally must
    /// hit the same wall a Production deployment would, or the ceiling is not actually enforced anywhere
    /// that catches a mistake before it ships.
    /// </summary>
    public static void ValidateAddressVerification(IConfiguration configuration, string environmentName)
    {
        var provider = configuration[$"{Geo.GeoOptions.SectionName}:Provider"] ?? "logging";

        var cacheHours = configuration.GetValue($"{Geo.GeoOptions.SectionName}:CacheHours", 24);
        if (cacheHours < 0 || cacheHours > 720)
            throw new InvalidOperationException(
                $"AddressVerification:CacheHours is {cacheHours}, outside the licensed 0–720 hour (30-day) " +
                "range for temporary caching of geocoder results under the standard Yandex Geocoder licence " +
                "(LEGAL_REVIEW.md §16.2/§16.5). This is a legal ceiling, not a performance knob — do not raise " +
                "it above 720 without a different licence. Set ADDRESSVERIFICATION__CACHEHOURS to a value in [0, 720].");

        if (string.Equals(provider, "logging", StringComparison.OrdinalIgnoreCase)) return;

        if (!string.Equals(provider, "yandex", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"AddressVerification:Provider is '{provider}', which is neither 'logging' nor 'yandex'. " +
                "Fix the configured value — see ARCHITECTURE_CYCLE13.md §206.");

        if (IsDeveloperEnvironment(environmentName)) return;

        var apiKey = configuration[$"{Geo.GeoOptions.SectionName}:Yandex:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "AddressVerification:Provider is 'yandex' but AddressVerification:Yandex:ApiKey is missing. " +
                "Set ADDRESSVERIFICATION__YANDEX__APIKEY in .env — see ARCHITECTURE_CYCLE13.md §206/§216 for " +
                "the full pre-enable checklist (licence variant chosen, licence purchased, CacheHours ≤ 720, " +
                "the matching privacy-policy paragraph published at the same moment).");
    }
}
