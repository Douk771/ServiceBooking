using Serilog;
using Serilog.Formatting.Compact;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.API.Startup;

/// <summary>
/// Fail-fast deployment checks, before and after <c>Build()</c>. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class DeploymentValidationExtensions
{
    /// <summary>The pre-Build checks; returns <c>isDeveloperEnvironment</c>, which the post-Build checks reuse.</summary>
    public static bool ValidateDeployment(this WebApplicationBuilder builder)
    {
    // Fail-fast on obviously-unsafe deployment configuration (US-10 → US-48, ARCHITECTURE.md §13). Runs
    // before anything reads these values, and BEFORE builder.Build() — so a misconfigured deployment never
    // finishes starting instead of silently running with a guessable/placeholder secret.
    // CustomWebApplicationFactory (tests) uses ASPNETCORE_ENVIRONMENT=Testing, so this never fires there.
    //
    // Allow-list, not deny-list (US-48, cycle C): an environment nobody told this code about yet (Staging,
    // Preview, Demo) must be treated as production-grade. Only the two environments we KNOW are developer
    // contexts are exempt — everything else gets the full set of checks, including anything introduced later.
    // The gate and the checks themselves live in DeploymentSafetyChecks (US-48 test-coverage gap, QA cycle
    // C) — pure, DI-free static methods so ServiceBooking.UnitTests can exercise them directly without
    // booting a host; this call site is unchanged behavior, just delegated.
    var isDeveloperEnvironment = DeploymentSafetyChecks.IsDeveloperEnvironment(builder.Environment.EnvironmentName);
    if (!isDeveloperEnvironment)
    {
        DeploymentSafetyChecks.ValidateSecrets(builder.Configuration, builder.Environment.ContentRootPath);
    }

    // Cycle 4 (US-54, US-35, US-30 — ARCHITECTURE_CYCLE4.md §24.2, §24.5, §34.3): all three are pure
    // config/filesystem checks with no dependency on the DI container, so — like ValidateSecrets above —
    // they run here, before Build(). Each is self-gated on environment/Enabled internally (unlike
    // ValidateSecrets they are NOT wrapped in `if (!isDeveloperEnvironment)`: ValidateNotificationSecrets'
    // rule 3 must run even in Development, and the other two are no-ops there anyway).
    DeploymentSafetyChecks.ValidateNotificationSecrets(builder.Configuration, builder.Environment.EnvironmentName);
    {
        // Reviewer note: this check's one non-fatal warning (a key rotation ack that's about to be
        // consumed, §24.4/§24.5) used to go through Console.WriteLine, which never reaches Serilog/GlitchTip
        // at all. The full pipeline (builder.Host.UseSerilog(...) above) is only wired up once the host is
        // actually built, which is AFTER this call by design (fail-fast before Build(), same as
        // ValidateSecrets) — so this is a minimal bootstrap logger, console-only, JUST for this one warning,
        // disposed immediately after. It intentionally does not duplicate the file/GlitchTip sinks above.
        using var bootstrapLogger = new LoggerConfiguration()
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateLogger();
        DeploymentSafetyChecks.ValidateChannelKeyFingerprint(
            builder.Configuration, builder.Environment.EnvironmentName, builder.Environment.ContentRootPath,
            warn: message => bootstrapLogger.Warning(message));
    }
    DeploymentSafetyChecks.ValidateTimeZoneDatabase(builder.Environment.EnvironmentName);
    DeploymentSafetyChecks.ValidateProviderDeliveryConsentMode(builder.Configuration);
    DeploymentSafetyChecks.ValidateGreenApiServerCountry(builder.Configuration);
    DeploymentSafetyChecks.ValidateRetentionPeriods(builder.Configuration);
    // ARCHITECTURE_CYCLE9.md §105.3 (проход C, Web Push мастеру) — own secret (VAPID), own provider switch,
    // checked the same "fail loud outside a developer environment" way as ValidateNotificationSecrets above,
    // but gated on ITS OWN Provider value, independent of Notifications:Provider.
    DeploymentSafetyChecks.ValidateStaffPushSecrets(builder.Configuration, builder.Environment.EnvironmentName);
    // Cycle 18 code-review finding — the once-only trial check (Д6) must be enabled and its key usable
    // outside a developer environment, or Production silently starts with either "no protection at all"
    // or "every activation permanently 409s". Same "fail loud outside dev" convention as the checks above.
    DeploymentSafetyChecks.ValidateTrialSecrets(builder.Configuration, builder.Environment.EnvironmentName);
    // ARCHITECTURE_CYCLE19.md §388.1/§388.4 — the geocoder (and its startup check) is removed in cycle 19;
    // address saving and the публичный правовой гейт stay (see CompanyAddressController).
    // ARCHITECTURE_CYCLE14.md §150.2 — own secret set (PHONEVERIFY_*), own provider switch
    // (PhoneVerification:Provider), checked unconditionally (even in Development — an unrecognized
    // provider value is a config-correctness bug there too, unlike the secrets themselves).
    DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(builder.Configuration, builder.Environment.EnvironmentName);
    // ARCHITECTURE_CYCLE25.md §498.1 — MAX messages to staff: when switched on, the bot, the encryption key and the chat-key HMAC are mandatory.
    DeploymentSafetyChecks.ValidateStaffMax(builder.Configuration, builder.Environment.EnvironmentName);
    // ARCHITECTURE_CYCLE23.md §391 — public base addresses of ezbook.ru / goods.ezbook.ru (fail-closed).
    DeploymentSafetyChecks.ValidatePublicSites(builder.Configuration, builder.Environment.EnvironmentName);
    DeploymentSafetyChecks.ValidateStaysPolicies(builder.Configuration);
    // ARCHITECTURE_CYCLE28.md §579.2 — lock 1 of demo mode: a no-op unless DemoMode:Enabled; then every address, the database, the JWT issuer and the providers must look like a demo.
    DeploymentSafetyChecks.ValidateDemoMode(builder.Configuration);

        return isDeveloperEnvironment;
    }

    public static void ValidateServiceRegistries(this WebApplication app)
    {
    // ARCHITECTURE_CYCLE9.md §104.2 (US-122) — "нераспознанное значение по-прежнему роняет старт; вдобавок
    // роняет старт ситуация «в реестре нет реализации для члена NotificationTransport»." Runs unconditionally
    // (every environment, including Development/Testing) — this is a CODE-correctness check (is every
    // NotificationTransport member actually wired up above), not a secrets/deployment-safety one, so it is
    // not gated by isDeveloperEnvironment the way ValidateNotificationSecrets is.
    using (var transportCheckScope = app.Services.CreateScope())
    {
        var transportRegistry = transportCheckScope.ServiceProvider
            .GetRequiredService<ServiceBooking.API.Services.Notifications.INotificationTransportRegistry>();
        DeploymentSafetyChecks.ValidateTransportRegistryCompleteness(
            nameof(ServiceBooking.API.Services.Notifications.INotificationTransportRegistry), transportRegistry.RegisteredTransports);

        var provisioningRegistry = transportCheckScope.ServiceProvider
            .GetRequiredService<ServiceBooking.API.Services.Notifications.IChannelProvisioningRegistry>();
        DeploymentSafetyChecks.ValidateTransportRegistryCompleteness(
            nameof(ServiceBooking.API.Services.Notifications.IChannelProvisioningRegistry), provisioningRegistry.RegisteredTransports);

        // ARCHITECTURE_CYCLE14.md §144.2 (Q2) — same shape, for the phone-verification method registry.
        var phoneVerificationRegistry = transportCheckScope.ServiceProvider
            .GetRequiredService<ServiceBooking.API.Services.PhoneVerification.IPhoneVerificationMethodRegistry>();
        DeploymentSafetyChecks.ValidateVerificationMethodRegistry(phoneVerificationRegistry.Registered);
    }
    }

    public static void ValidateDeploymentAfterBuild(this WebApplication app, WebApplicationBuilder builder, bool isDeveloperEnvironment)
    {
    // Two more fail-fast checks (US-42, US-36 → US-48, ARCHITECTURE.md §13), added in cycle C. Unlike the
    // block above, both need a constructed service provider (IPNetwork parsing for the first is already
    // done by ForwardedHeadersOptions above; loading legal.json goes through LegalDocumentProvider), so they
    // run here, after builder.Build(), rather than being folded into the pre-Build block.
    if (!isDeveloperEnvironment)
    {
        // Delegated to DeploymentSafetyChecks (see the pre-Build block above for why) — this one doesn't
        // actually need the constructed service provider either, it just historically ran alongside the
        // legal-document check below, which does.
        DeploymentSafetyChecks.ValidateTrustedNetworksConfigured(builder.Configuration);

        using var legalCheckScope = app.Services.CreateScope();
        var legalProvider = legalCheckScope.ServiceProvider.GetRequiredService<LegalDocumentProvider>();
        legalProvider.LoadAtStartup();
        if (legalProvider.Current is null)
            throw new InvalidOperationException(
                "Legal documents (App_Data/legal/legal.json) failed to load — without all five document " +
                "types and all six interface texts (ARCHITECTURE_CYCLE5.md §43.3) the service cannot legally " +
                "accept registrations. Check the container logs above for the specific validation error and " +
                "fix legal.json or the mounted files.");
    }
    }
}
