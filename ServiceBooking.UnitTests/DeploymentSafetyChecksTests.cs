using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// US-48 coverage gap closed (QA cycle C, TEST_CATALOG.md "SEC-042/SEC-042b" note): Program.cs's
/// deployment fail-fast checks previously had zero automated coverage, because the logic lived as
/// top-level statements entangled with WebApplicationBuilder. Now that it's extracted into
/// DeploymentSafetyChecks (pure static methods over IConfiguration + plain strings), these tests call it
/// directly — an in-memory IConfigurationRoot, no host, no HTTP, no database.
/// </summary>
public class DeploymentSafetyChecksTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // A configuration that would pass every check in ValidateSecrets, used as the "known-good" baseline
    // that individual tests below mutate one key at a time — keeps each test's diff to exactly the thing
    // it's proving matters.
    private static Dictionary<string, string?> ValidSecrets(string contentRoot) => new()
    {
        ["Jwt:Key"] = "A_REAL_SECRET_KEY_THAT_IS_AT_LEAST_32_CHARACTERS_LONG",
        ["SuperAdmin:Password"] = "Real-Passw0rd",
        ["SuperAdmin:Phone"] = "+79161234567",
        ["Storage:PrivateRoot"] = Path.Combine(contentRoot, "App_Data", "private-uploads")
    };

    // ── IsDeveloperEnvironment: allow-list, not deny-list (US-48) ──────────────────────────────────

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("development", true)]  // IHostEnvironment.IsEnvironment is case-insensitive
    [InlineData("TESTING", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]     // the whole point of US-48: an environment nobody named explicitly
    [InlineData("Preview", false)]     // is NOT assumed safe just because it isn't "Production"
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDeveloperEnvironment_OnlyDevelopmentAndTestingAreExempt(string? environmentName, bool expected) =>
        DeploymentSafetyChecks.IsDeveloperEnvironment(environmentName).Should().Be(expected);

    // ── ValidateSecrets: Jwt:Key ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateSecrets_ValidConfiguration_DoesNotThrow()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var config = BuildConfig(ValidSecrets(contentRoot));

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]                                            // missing entirely
    [InlineData("")]                                              // present but empty
    [InlineData("short")]                                         // < 32 chars
    [InlineData("CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS")] // the literal appsettings.json placeholder
    public void ValidateSecrets_WeakOrPlaceholderJwtKey_ThrowsBeforeAnythingStarts(string? jwtKey)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values["Jwt:Key"] = jwtKey;
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Key*");
    }

    // ── ValidateSecrets: SuperAdmin:Password ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]           // missing entirely
    [InlineData("")]             // present but empty
    [InlineData("Admin12345")]   // the literal appsettings.json placeholder
    [InlineData("CHANGE_ME")]    // the literal .env.production.example placeholder
    public void ValidateSecrets_PlaceholderSuperAdminPassword_Throws(string? password)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values["SuperAdmin:Password"] = password;
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*SuperAdmin:Password*");
    }

    [Fact]
    public void ValidateSecrets_PlaceholderSuperAdminPhone_WarnsButDoesNotThrow()
    {
        // SuperAdmin:Phone is deliberately NOT fatal (unlike Password/Jwt:Key) — a deployment that
        // forgot to override it stays reachable, just with a foreseeable login. This asserts both
        // halves of that contract: it must not stop the app, and it must still be surfaced somewhere an
        // operator would see it.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values["SuperAdmin:Phone"] = "+70000000000";
        var config = BuildConfig(values);
        var warnings = new List<string>();

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, warnings.Add);

        act.Should().NotThrow();
        warnings.Should().ContainSingle(w => w.Contains("SuperAdmin:Phone"));
    }

    // ── ValidateSecrets: Storage:PrivateRoot containment ───────────────────────────────────────────

    [Fact]
    public void ValidateSecrets_PrivateRootInsideWwwroot_Throws()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values["Storage:PrivateRoot"] = Path.Combine(contentRoot, "wwwroot", "private-uploads");
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*wwwroot*");
    }

    [Fact]
    public void ValidateSecrets_UnsetPrivateRoot_DefaultsOutsideWwwroot_DoesNotThrow()
    {
        // Storage:PrivateRoot absent entirely (not just an override) resolves to
        // {contentRoot}/App_Data/private-uploads, which is a sibling of wwwroot, not inside it — this is
        // the out-of-the-box configuration and must stay safe without anyone setting anything.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values.Remove("Storage:PrivateRoot");
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().NotThrow();
    }

    // ── ValidateSecrets: Storage:PrivateRoot vs Storage:PublicRoot (custom public root) ───────────────

    [Fact]
    public void ValidateSecrets_PrivateRootInsideCustomPublicRoot_Throws()
    {
        // Storage__PublicRoot=/srv/ezbook/uploads with Storage__PrivateRoot=/srv/ezbook/uploads/private
        // used to pass silently before this check existed — UseStaticFiles now serves whatever
        // Storage:PublicRoot points to (FileStorage.PublicRootFullPath), not wwwroot, so this must be
        // caught even though it's nowhere near wwwroot.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var customPublicRoot = Path.Combine(contentRoot, "srv-uploads");
        var values = ValidSecrets(contentRoot);
        values["Storage:PublicRoot"] = customPublicRoot;
        values["Storage:PrivateRoot"] = Path.Combine(customPublicRoot, "private");
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*PrivateRoot resolves inside Storage:PublicRoot*");
    }

    [Fact]
    public void ValidateSecrets_PrivateRootEqualsPublicRoot_Throws()
    {
        // Both roots resolving to the very same directory is the worst case: every uploaded private
        // file would be reachable at /uploads/<key>. A StartsWith(root + separator) containment check
        // alone does not catch two equal paths (neither is a strict prefix of the other), so this needs
        // its own equality branch.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var sharedRoot = Path.Combine(contentRoot, "shared-uploads");
        var values = ValidSecrets(contentRoot);
        values["Storage:PublicRoot"] = sharedRoot;
        values["Storage:PrivateRoot"] = sharedRoot;
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*PrivateRoot resolves inside Storage:PublicRoot*");
    }

    [Fact]
    public void ValidateSecrets_UnsetPublicRoot_DefaultsToWwwrootUploads_PrivateRootOutside_DoesNotThrow()
    {
        // The out-of-the-box configuration: Storage:PublicRoot absent resolves to
        // {contentRoot}/wwwroot/uploads, and the default private root
        // ({contentRoot}/App_Data/private-uploads) is neither inside it nor equal to it. This must stay
        // safe without anyone setting Storage:PublicRoot at all — matches every configuration actually
        // shipped in the repo (docker-compose.prod.yml, .env.production.example, appsettings.json).
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values.Remove("Storage:PublicRoot");
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSecrets_PrivateRootInsideWwwroot_StillThrows_WithCustomPublicRootUnset()
    {
        // The pre-existing wwwroot-containment branch must keep working exactly as before: this is the
        // same scenario as ValidateSecrets_PrivateRootInsideWwwroot_Throws above, just re-asserted here
        // to pin that adding the new Storage:PublicRoot branch didn't change its behavior or the order
        // in which the two checks run.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values["Storage:PrivateRoot"] = Path.Combine(contentRoot, "wwwroot", "private-uploads");
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*wwwroot*");
    }

    // ── ValidateSecrets: Storage:PublicRoot vs the application's own content root ─────────────────────

    [Fact]
    public void ValidateSecrets_PublicRootEqualsContentRoot_Throws()
    {
        // Storage__PublicRoot=/app (a typo, or "make uploads just work") makes UseStaticFiles serve the
        // whole application directory at /uploads/... — appsettings.Production.json, the compiled DLLs,
        // App_Data/legal/... . Before the sanitation cycle this couldn't happen because UseStaticFiles
        // was hard-wired to wwwroot regardless of configuration. Deliberately uses ValidSecrets'
        // default/untouched Storage:PrivateRoot ({contentRoot}/App_Data/private-uploads) rather than an
        // artificial one outside contentRoot: that private root is a descendant of the public root once
        // Storage:PublicRoot equals contentRoot, so this is exactly the mistyped Production configuration
        // (Storage__PublicRoot=/app, Storage__PrivateRoot=/app/private-uploads) where BOTH the
        // content-root check and the private-vs-public check would fire — proving the reordering (review
        // round 2) makes the content-root check win and report the true root cause first.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var values = ValidSecrets(contentRoot);
        values["Storage:PublicRoot"] = contentRoot;
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Storage:PublicRoot*content root*");
    }

    [Fact]
    public void ValidateSecrets_PublicRootIsAncestorOfContentRoot_Throws()
    {
        // Pointing even higher than the content root itself (e.g. "/srv" when the app lives at
        // "/srv/ezbook") is strictly worse than the equality case above and must be caught the same way.
        // Same reasoning as ValidateSecrets_PublicRootEqualsContentRoot_Throws above for keeping
        // ValidSecrets' default Storage:PrivateRoot untouched: it stays a descendant of both contentRoot
        // and the ancestor public root, so this also exercises the reachable, shipped-shape scenario
        // rather than an artificial private root nobody's configuration uses.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid(), "app");
        var ancestorOfContentRoot = Path.GetDirectoryName(contentRoot)!;
        var values = ValidSecrets(contentRoot);
        values["Storage:PublicRoot"] = ancestorOfContentRoot;
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Storage:PublicRoot*content root*");
    }

    [Fact]
    public void ValidateSecrets_CustomPublicRootOutsideContentRoot_DoesNotThrow()
    {
        // A dedicated uploads directory that is neither inside the app's content root nor an ancestor of
        // it (e.g. /srv/ezbook/uploads next to an app deployed at /srv/ezbook/app) is exactly the
        // legitimate use case Storage:PublicRoot exists for and must keep working.
        var contentRootParent = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var contentRoot = Path.Combine(contentRootParent, "app");
        var dedicatedPublicRoot = Path.Combine(contentRootParent, "uploads");
        var values = ValidSecrets(contentRoot);
        values["Storage:PublicRoot"] = dedicatedPublicRoot;
        var config = BuildConfig(values);

        var act = () => DeploymentSafetyChecks.ValidateSecrets(config, contentRoot, _ => { });

        act.Should().NotThrow();
    }

    // ── ValidateTrustedNetworksConfigured ──────────────────────────────────────────────────────────

    [Fact]
    public void ValidateTrustedNetworksConfigured_EmptyList_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => DeploymentSafetyChecks.ValidateTrustedNetworksConfigured(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ForwardedHeaders:TrustedNetworks*");
    }

    [Fact]
    public void ValidateTrustedNetworksConfigured_ConfiguredNetwork_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:TrustedNetworks:0"] = "172.18.0.0/16"
        });

        var act = () => DeploymentSafetyChecks.ValidateTrustedNetworksConfigured(config);

        act.Should().NotThrow();
    }

    // ── The exact scenario QA flagged as uncovered: weak defaults outside Development/Testing ────────

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("SomeEnvironmentNobodyNamedYet")]
    public void App_WithWeakDefaults_OutsideDevOrTesting_RefusesToStart(string environmentName)
    {
        // Mirrors Program.cs's own gating exactly: `if (!isDeveloperEnvironment) { ValidateSecrets(...) }`
        // — appsettings.json's own out-of-the-box values (Jwt:Key unset, SuperAdmin:Password the literal
        // "Admin12345" placeholder) are what a deployment that forgot to override .env would actually run
        // with.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var weakDefaults = BuildConfig(new Dictionary<string, string?>
        {
            ["SuperAdmin:Password"] = "Admin12345",
            ["SuperAdmin:Phone"] = "+70000000000"
            // Jwt:Key intentionally absent — same as a fresh checkout with no .env applied.
        });

        DeploymentSafetyChecks.IsDeveloperEnvironment(environmentName).Should().BeFalse(
            "US-48 treats every environment other than Development/Testing as production-grade");

        var act = () => DeploymentSafetyChecks.ValidateSecrets(weakDefaults, contentRoot, _ => { });

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void App_WithEmptyTrustedNetworks_OutsideDevOrTesting_RefusesToStart(string environmentName)
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        DeploymentSafetyChecks.IsDeveloperEnvironment(environmentName).Should().BeFalse();

        var act = () => DeploymentSafetyChecks.ValidateTrustedNetworksConfigured(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*TrustedNetworks*");
    }

    // ── ValidateNotificationSecrets (cycle 4, US-54/US-35, ARCHITECTURE_CYCLE4.md §24.2) ──────────

    private static string ValidBase64Key() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    // I4: NOTIFICATIONS_UNSUBSCRIBE_KEY/NOTIFICATIONS_WEBHOOK_TOKEN, present on every "should not throw"
    // config below a real-provider gate — the two new checks these tests exist to NOT trip on the
    // otherwise-valid configurations.
    private static Dictionary<string, string?> ValidRealProviderExtras() => new()
    {
        ["Notifications:UnsubscribeKey"] = "a-real-unsubscribe-hmac-key",
        ["Notifications:WebhookToken"] = "a-real-webhook-token",
        // ARCHITECTURE_CYCLE9.md §104.1: MAX is a separate GREEN-API partner account, checked the same
        // way as Notifications:PartnerToken.
        ["Notifications:GreenApiMax:PartnerToken"] = "a-real-max-partner-token",
    };

    [Fact]
    public void ValidateNotificationSecrets_LoggingProviderInProduction_DoesNotThrow()
    {
        // I4: gated on Provider, not Notifications:Enabled (which no longer gates anything downstream)
        // — the "logging" provider (default, and the only one exempt) never touches a real secret.
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:Provider"] = "logging" });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotificationSecrets_NoProviderConfiguredInProduction_DefaultsToLogging_DoesNotThrow()
    {
        // An absent Notifications:Provider key must read exactly like NotificationOptions.Provider's own
        // default ("logging"), not like "some other, unrecognized provider" that happens to need checking.
        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotificationSecrets_GreenApiInTesting_SkipsKeyValidation()
    {
        // Developer environments are exempt from rules 1–2 (unlike rule 3 below).
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:Provider"] = "green-api" });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Testing");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CHANGE_ME")]
    [InlineData("not-valid-base64!!!")]
    public void ValidateNotificationSecrets_GreenApiInProduction_MissingOrPlaceholderKey_Throws(string? key)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:Provider"] = "green-api",
            ["Notifications:EncryptionKey"] = key
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*EncryptionKey*");
    }

    [Fact]
    public void ValidateNotificationSecrets_GreenApiInProduction_WrongKeyLength_Throws()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:Provider"] = "green-api",
            ["Notifications:EncryptionKey"] = shortKey
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
    }

    [Fact]
    public void ValidateNotificationSecrets_GreenApiInProduction_ValidKey_WithoutPartnerToken_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>(ValidRealProviderExtras())
        {
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
            ["Notifications:Provider"] = "green-api"
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*PartnerToken*");
    }

    // ARCHITECTURE_CYCLE9.md §104.1: MAX is a separate GREEN-API partner account and needs its own
    // token, checked by the same rule (and failure mode) as WhatsApp's Notifications:PartnerToken above.
    [Fact]
    public void ValidateNotificationSecrets_GreenApiInProduction_ValidWhatsAppToken_WithoutMaxPartnerToken_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>(ValidRealProviderExtras())
        {
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
            ["Notifications:Provider"] = "green-api",
            ["Notifications:PartnerToken"] = "a-real-partner-token",
            ["Notifications:GreenApiMax:PartnerToken"] = null,
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*GreenApiMax:PartnerToken*");
    }

    [Fact]
    public void ValidateNotificationSecrets_GreenApiInProduction_MaxPartnerTokenIsPlaceholder_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>(ValidRealProviderExtras())
        {
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
            ["Notifications:Provider"] = "green-api",
            ["Notifications:PartnerToken"] = "a-real-partner-token",
            ["Notifications:GreenApiMax:PartnerToken"] = "CHANGE_ME",
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*GreenApiMax:PartnerToken*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void ValidateNotificationSecrets_MaxPartnerTokenSetOutsideProduction_Throws(string environmentName)
    {
        // Same mirror-image rule as Notifications:PartnerToken — a real MAX partner token on a
        // dev/staging machine could create/delete a live salon's MAX instance.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:GreenApiMax:PartnerToken"] = "a-real-max-partner-token",
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, environmentName);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GreenApiMax:PartnerToken*");
    }

    [Fact]
    public void ValidateNotificationSecrets_EmptyMaxPartnerTokenOutsideProduction_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:GreenApiMax:PartnerToken"] = "" });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Development");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotificationSecrets_GreenApiInProduction_ValidConfig_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>(ValidRealProviderExtras())
        {
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
            ["Notifications:Provider"] = "green-api",
            ["Notifications:PartnerToken"] = "a-real-partner-token"
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().NotThrow();
    }

    // I4: a real provider with everything else valid but an empty UnsubscribeKey/WebhookToken must still
    // fail fast — the downstream symptom (message shipped without the opt-out line; webhook 401s
    // forever) never surfaces as a crash on its own.
    [Fact]
    public void ValidateNotificationSecrets_GreenApiInProduction_MissingUnsubscribeKey_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
            ["Notifications:Provider"] = "green-api",
            ["Notifications:PartnerToken"] = "a-real-partner-token",
            ["Notifications:GreenApiMax:PartnerToken"] = "a-real-max-partner-token",
            ["Notifications:WebhookToken"] = "a-real-webhook-token",
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*UnsubscribeKey*");
    }

    [Fact]
    public void ValidateNotificationSecrets_GreenApiInProduction_MissingWebhookToken_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
            ["Notifications:Provider"] = "green-api",
            ["Notifications:PartnerToken"] = "a-real-partner-token",
            ["Notifications:GreenApiMax:PartnerToken"] = "a-real-max-partner-token",
            ["Notifications:UnsubscribeKey"] = "a-real-unsubscribe-hmac-key",
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*WebhookToken*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void ValidateNotificationSecrets_PartnerTokenSetOutsideProduction_Throws(string environmentName)
    {
        // Rule 3 (§24.2 p.3) is a mirror-image safety rule and is NOT exempted for developer
        // environments: a real partner token on a dev machine could delete a live salon's instance.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:PartnerToken"] = "a-real-partner-token"
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, environmentName);

        act.Should().Throw<InvalidOperationException>().WithMessage("*PartnerToken*");
    }

    [Fact]
    public void ValidateNotificationSecrets_PartnerTokenSetInProduction_DoesNotTriggerRule3()
    {
        var config = BuildConfig(new Dictionary<string, string?>(ValidRealProviderExtras())
        {
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
            ["Notifications:Provider"] = "green-api",
            ["Notifications:PartnerToken"] = "a-real-partner-token"
        });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotificationSecrets_EmptyPartnerTokenOutsideProduction_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:PartnerToken"] = "" });

        var act = () => DeploymentSafetyChecks.ValidateNotificationSecrets(config, "Development");

        act.Should().NotThrow();
    }

    // ── ValidateChannelKeyFingerprint (cycle 4, US-54, ARCHITECTURE_CYCLE4.md §24.5) ────────────────

    [Fact]
    public void ValidateChannelKeyFingerprint_LoggingProvider_DoesNotTouchFileSystem()
    {
        // I4: gated on Provider, same as ValidateNotificationSecrets — "logging" needs no fingerprint.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:Provider"] = "logging" });

        var act = () => DeploymentSafetyChecks.ValidateChannelKeyFingerprint(config, "Production", contentRoot);

        act.Should().NotThrow();
        Directory.Exists(contentRoot).Should().BeFalse();
    }

    [Fact]
    public void ValidateChannelKeyFingerprint_GreenApiInTesting_IsNoOp()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:Provider"] = "green-api",
            ["Notifications:EncryptionKey"] = ValidBase64Key()
        });

        var act = () => DeploymentSafetyChecks.ValidateChannelKeyFingerprint(config, "Testing", contentRoot);

        act.Should().NotThrow();
        Directory.Exists(contentRoot).Should().BeFalse();
    }

    [Fact]
    public void ValidateChannelKeyFingerprint_FirstRunInProduction_WritesFingerprintFile()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Notifications:Provider"] = "green-api",
                ["Notifications:EncryptionKey"] = ValidBase64Key()
            });

            DeploymentSafetyChecks.ValidateChannelKeyFingerprint(config, "Production", contentRoot);

            var expectedPath = Path.Combine(contentRoot, "App_Data", "state", ".notifications-key-fingerprint");
            File.Exists(expectedPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateChannelKeyFingerprint_KeyChangedWithoutAck_ThrowsAndLeavesFileUntouched()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "sb-safety-" + Guid.NewGuid());
        try
        {
            var firstKeyConfig = BuildConfig(new Dictionary<string, string?>
            {
                ["Notifications:Provider"] = "green-api",
                ["Notifications:EncryptionKey"] = ValidBase64Key()
            });
            DeploymentSafetyChecks.ValidateChannelKeyFingerprint(firstKeyConfig, "Production", contentRoot);

            var secondKeyConfig = BuildConfig(new Dictionary<string, string?>
            {
                ["Notifications:Provider"] = "green-api",
                ["Notifications:EncryptionKey"] = ValidBase64Key() // a DIFFERENT random key
            });

            var act = () => DeploymentSafetyChecks.ValidateChannelKeyFingerprint(secondKeyConfig, "Production", contentRoot);

            act.Should().Throw<InvalidOperationException>().WithMessage("*NOTIFICATIONS_ENCRYPTION_KEY*");
        }
        finally
        {
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
        }
    }

    // ── ValidateTimeZoneDatabase (cycle 4, US-30, ARCHITECTURE_CYCLE4.md §34.3) ────────────────────

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ValidateTimeZoneDatabase_DeveloperEnvironment_DoesNotThrow(string environmentName)
    {
        var act = () => DeploymentSafetyChecks.ValidateTimeZoneDatabase(environmentName);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTimeZoneDatabase_Production_ResolvesAsiaBarnaul_OnThisMachine()
    {
        // Documents the requirement rather than faking a broken tzdata (that would need mocking
        // TimeZoneInfo, which isn't practical) — the real assertion this check protects against is
        // exercised in deploy/ci/smoke.sh against the actual runtime image (§34.3).
        var act = () => DeploymentSafetyChecks.ValidateTimeZoneDatabase("Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ParseDefaultWorkWindow_ValidConfig_ReturnsParsedTimes()
    {
        var config = BuildConfig(new()
        {
            ["Booking:DefaultWorkWindow:Start"] = "09:00",
            ["Booking:DefaultWorkWindow:End"] = "21:00",
        });

        var (start, end) = DeploymentSafetyChecks.ParseDefaultWorkWindow(config);

        start.Should().Be(new TimeOnly(9, 0));
        end.Should().Be(new TimeOnly(21, 0));
    }

    [Fact]
    public void ParseDefaultWorkWindow_Missing_Throws()
    {
        var config = BuildConfig(new());
        var act = () => DeploymentSafetyChecks.ParseDefaultWorkWindow(config);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ParseDefaultWorkWindow_Unparsable_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Booking:DefaultWorkWindow:Start"] = "not-a-time",
            ["Booking:DefaultWorkWindow:End"] = "21:00",
        });
        var act = () => DeploymentSafetyChecks.ParseDefaultWorkWindow(config);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ParseDefaultWorkWindow_StartAfterEnd_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Booking:DefaultWorkWindow:Start"] = "22:00",
            ["Booking:DefaultWorkWindow:End"] = "09:00",
        });
        var act = () => DeploymentSafetyChecks.ParseDefaultWorkWindow(config);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── ValidateTransportRegistryCompleteness (cycle 9, US-122, ARCHITECTURE_CYCLE9.md §104.2) ──────

    [Fact]
    public void ValidateTransportRegistryCompleteness_EveryMemberRegistered_DoesNotThrow()
    {
        var act = () => DeploymentSafetyChecks.ValidateTransportRegistryCompleteness(
            "TestRegistry", Enum.GetValues<NotificationTransport>());

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTransportRegistryCompleteness_MissingMember_ThrowsNamingTheRegistryAndTheGap()
    {
        // Simulates a third transport being added to the enum without a matching adapter registration —
        // the exact scenario this check exists to catch loud at startup (§104.2: "в реестре нет
        // реализации для члена NotificationTransport").
        var act = () => DeploymentSafetyChecks.ValidateTransportRegistryCompleteness(
            "INotificationTransportRegistry", [NotificationTransport.WhatsApp]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*INotificationTransportRegistry*")
            .WithMessage("*Max*");
    }

    [Fact]
    public void ValidateTransportRegistryCompleteness_EmptyRegistry_ThrowsNamingEveryMember()
    {
        var act = () => DeploymentSafetyChecks.ValidateTransportRegistryCompleteness(
            "IChannelProvisioningRegistry", []);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WhatsApp*")
            .WithMessage("*Max*");
    }

    // ── ValidateProviderDeliveryConsentMode (cycle 5, T-24, ARCHITECTURE_CYCLE5.md §52.3) ──────────

    [Theory]
    [InlineData("Strict")]
    [InlineData("AccountsOnly")]
    [InlineData("Off")]
    [InlineData("strict")] // case-insensitive
    [InlineData(null)]     // absent → NotificationOptions' own AccountsOnly default
    [InlineData("")]
    public void ValidateProviderDeliveryConsentMode_RecognizedOrAbsentValue_DoesNotThrow(string? value)
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:ProviderDeliveryConsent"] = value });
        var act = () => DeploymentSafetyChecks.ValidateProviderDeliveryConsentMode(config);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProviderDeliveryConsentMode_UnrecognizedValue_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:ProviderDeliveryConsent"] = "Nonsense" });
        var act = () => DeploymentSafetyChecks.ValidateProviderDeliveryConsentMode(config);
        act.Should().Throw<InvalidOperationException>().WithMessage("*ProviderDeliveryConsent*");
    }

    // ── ValidateGreenApiServerCountry (cycle 5, T5-B13, ARCHITECTURE_CYCLE5.md §52.1) ───────────────

    [Fact]
    public void ValidateGreenApiServerCountry_CreationDisabled_EmptyCountry_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:GreenApi:InstanceCreationEnabled"] = "false",
            ["Notifications:GreenApi:ServerCountry"] = ""
        });
        var act = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(config);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateGreenApiServerCountry_CreationEnabled_EmptyCountry_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:GreenApi:InstanceCreationEnabled"] = "true",
            ["Notifications:GreenApi:ServerCountry"] = ""
        });
        var act = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(config);
        act.Should().Throw<InvalidOperationException>().WithMessage("*ServerCountry*");
    }

    [Fact]
    public void ValidateGreenApiServerCountry_CreationEnabled_CountrySet_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:GreenApi:InstanceCreationEnabled"] = "true",
            ["Notifications:GreenApi:ServerCountry"] = "RU"
        });
        var act = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(config);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateGreenApiServerCountry_CreationEnabled_WhitespaceCountry_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:GreenApi:InstanceCreationEnabled"] = "true",
            ["Notifications:GreenApi:ServerCountry"] = "   "
        });
        var act = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(config);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── ValidateRetentionPeriods (T5-B8/B9, ARCHITECTURE_CYCLE5.md §49.1/§49.5) ────────────────

    [Fact]
    public void ValidateRetentionPeriods_DefaultConfig_DoesNotThrow()
    {
        // No Retention section at all — must fall back to RetentionPeriods' own defaults (365/1095),
        // both of which already satisfy the minimums.
        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => DeploymentSafetyChecks.ValidateRetentionPeriods(config);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(365)]
    [InlineData(1095)]
    [InlineData(3650)]
    public void ValidateRetentionPeriods_TemplateHistoryAtOrAboveMinimum_DoesNotThrow(int days)
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Retention:TemplateHistoryDays"] = days.ToString() });

        var act = () => DeploymentSafetyChecks.ValidateRetentionPeriods(config);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(364)]
    [InlineData(0)]
    [InlineData(1)]
    public void ValidateRetentionPeriods_TemplateHistoryBelowMinimum_Throws(int days)
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Retention:TemplateHistoryDays"] = days.ToString() });

        var act = () => DeploymentSafetyChecks.ValidateRetentionPeriods(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*TemplateHistoryDays*");
    }

    [Theory]
    [InlineData(1095)]
    [InlineData(1096)]
    [InlineData(3650)]
    public void ValidateRetentionPeriods_ConsentRecordAtOrAboveMinimum_DoesNotThrow(int days)
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Retention:ConsentRecordDays"] = days.ToString() });

        var act = () => DeploymentSafetyChecks.ValidateRetentionPeriods(config);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(1094)]
    [InlineData(365)]
    [InlineData(0)]
    public void ValidateRetentionPeriods_ConsentRecordBelowMinimum_Throws(int days)
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Retention:ConsentRecordDays"] = days.ToString() });

        var act = () => DeploymentSafetyChecks.ValidateRetentionPeriods(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConsentRecordDays*");
    }

    [Fact]
    public void ValidateRetentionPeriods_TemplateHistoryChecked_EvenWhenConsentRecordAlsoInvalid()
    {
        // Whichever fails first is fine — the point is that a caller fixing one doesn't get a false
        // "all clear" while the other minimum is still violated.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Retention:TemplateHistoryDays"] = "30",
            ["Retention:ConsentRecordDays"] = "30",
        });

        var act = () => DeploymentSafetyChecks.ValidateRetentionPeriods(config);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── ValidateStaffPushSecrets: ARCHITECTURE_CYCLE9.md §105.3 (Q15, R12) ─────────────────────────

    private static (string PublicKey, string PrivateKey) GenerateValidVapidPair()
    {
        using var ecdh = System.Security.Cryptography.ECDiffieHellman.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var parameters = ecdh.ExportParameters(includePrivateParameters: true);

        var publicKeyBytes = new byte[65];
        publicKeyBytes[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X!, 0, publicKeyBytes, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y!, 0, publicKeyBytes, 33, 32);

        static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (Base64Url(publicKeyBytes), Base64Url(parameters.D!));
    }

    [Fact]
    public void ValidateStaffPushSecrets_DefaultLoggingProvider_DoesNotThrow_InProduction()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateStaffPushSecrets_UnrecognizedProvider_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:StaffPush:Provider"] = "carrier-pigeon" });
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*carrier-pigeon*");
    }

    [Fact]
    public void ValidateStaffPushSecrets_WebPushInProduction_MissingKeys_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:StaffPush:Provider"] = "web-push" });
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*VapidPublicKey*");
    }

    [Fact]
    public void ValidateStaffPushSecrets_WebPushInProduction_MalformedKeys_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:StaffPush:Provider"] = "web-push",
            ["Notifications:StaffPush:VapidPublicKey"] = "not-a-real-key",
            ["Notifications:StaffPush:VapidPrivateKey"] = "also-not-a-real-key",
            ["Notifications:StaffPush:VapidSubject"] = "mailto:ops@ezbook.ru",
        });
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, "Production");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateStaffPushSecrets_WebPushInProduction_ValidKeysButNoSubject_Throws()
    {
        var (publicKey, privateKey) = GenerateValidVapidPair();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:StaffPush:Provider"] = "web-push",
            ["Notifications:StaffPush:VapidPublicKey"] = publicKey,
            ["Notifications:StaffPush:VapidPrivateKey"] = privateKey,
        });
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*VapidSubject*");
    }

    [Fact]
    public void ValidateStaffPushSecrets_WebPushInProduction_ValidConfiguration_DoesNotThrow()
    {
        var (publicKey, privateKey) = GenerateValidVapidPair();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:StaffPush:Provider"] = "web-push",
            ["Notifications:StaffPush:VapidPublicKey"] = publicKey,
            ["Notifications:StaffPush:VapidPrivateKey"] = privateKey,
            ["Notifications:StaffPush:VapidSubject"] = "mailto:ops@ezbook.ru",
            ["Notifications:EncryptionKey"] = ValidBase64Key(),
        });
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateStaffPushSecrets_WebPushInProduction_ValidVapidButNoEncryptionKey_Throws()
    {
        // B5/§105.3: Notifications:Provider=logging (no real WhatsApp/MAX provider configured) never
        // reaches ValidateNotificationSecrets' own EncryptionKey check, so a deployment that only turns
        // ON web-push must be caught here instead — otherwise it starts clean and only 500s on the
        // first POST /api/push/subscriptions once a master actually subscribes.
        var (publicKey, privateKey) = GenerateValidVapidPair();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Notifications:StaffPush:Provider"] = "web-push",
            ["Notifications:StaffPush:VapidPublicKey"] = publicKey,
            ["Notifications:StaffPush:VapidPrivateKey"] = privateKey,
            ["Notifications:StaffPush:VapidSubject"] = "mailto:ops@ezbook.ru",
        });
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*EncryptionKey*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ValidateStaffPushSecrets_WebPushInDeveloperEnvironment_MissingKeys_DoesNotThrow(string environmentName)
    {
        // §105.3: developer environments are expected to churn keys freely, same carve-out as
        // ValidateNotificationSecrets' rules 1-2.
        var config = BuildConfig(new Dictionary<string, string?> { ["Notifications:StaffPush:Provider"] = "web-push" });
        var act = () => DeploymentSafetyChecks.ValidateStaffPushSecrets(config, environmentName);
        act.Should().NotThrow();
    }

    // ── ValidateAddressVerification: ARCHITECTURE_CYCLE13.md §206/§209.2 ──────────────────────────────

    [Fact]
    public void ValidateAddressVerification_DefaultLoggingProvider_DoesNotThrow_InProduction()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Production");
        act.Should().NotThrow();
    }

    // ── ValidatePhoneVerificationSecrets (ARCHITECTURE_CYCLE14.md §150.2) ──────────────────────────

    private static Dictionary<string, string?> ValidPhoneVerificationSecrets() => new()
    {
        ["PhoneVerification:Provider"] = "max-bot",
        ["PhoneVerification:Max:BotToken"] = "real-bot-token",
        ["PhoneVerification:Max:BotUsername"] = "ezbookbot",
        ["PhoneVerification:Max:WebhookToken"] = new string('a', 32),
        ["PhoneVerification:Max:PublicBaseUrl"] = "https://ezbook.ru",
        ["PhoneVerification:ExternalKeyHmac"] = Convert.ToBase64String(new byte[32]),
    };

    [Fact]
    public void ValidatePhoneVerificationSecrets_StubProvider_RequiresNothing()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["PhoneVerification:Provider"] = "stub" });
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(config, "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateAddressVerification_UnrecognizedProvider_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:Provider"] = "2gis" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Provider*");
    }

    [Fact]
    public void ValidateAddressVerification_YandexInProduction_MissingApiKey_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:Provider"] = "yandex" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiKey*");
    }

    [Fact]
    public void ValidateAddressVerification_YandexInProduction_WithApiKey_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AddressVerification:Provider"] = "yandex",
            ["AddressVerification:Yandex:ApiKey"] = "real-key",
        });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidatePhoneVerificationSecrets_UnsetProvider_DefaultsToStub_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(config, "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidatePhoneVerificationSecrets_UnrecognizedProvider_ThrowsEvenInDevelopment()
    {
        // Unlike the secrets checks, an unrecognized Provider value fails EVERYWHERE, including
        // Development — it's a code/config-correctness bug, not something a dev machine should tolerate.
        var config = BuildConfig(new Dictionary<string, string?> { ["PhoneVerification:Provider"] = "sms" });
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(config, "Development");
        act.Should().Throw<InvalidOperationException>().WithMessage("*sms*");
    }

    [Fact]
    public void ValidatePhoneVerificationSecrets_MaxBotInProduction_ValidConfig_DoesNotThrow()
    {
        var config = BuildConfig(ValidPhoneVerificationSecrets());
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(config, "Production");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ValidateAddressVerification_YandexInDeveloperEnvironment_MissingApiKey_DoesNotThrow(string environmentName)
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:Provider"] = "yandex" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, environmentName);
        act.Should().NotThrow();
    }

    // §209.2/§218 R23: the licensed ceiling on caching a geocoder result is 720 hours (30 days) — this
    // is checked in EVERY environment, unlike the Provider/ApiKey checks above, because it is a fact
    // about the licence, not a Production-only safety net.
    [Fact]
    public void ValidateAddressVerification_CacheHours721_ThrowsEvenInDevelopment()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:CacheHours"] = "721" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Development");
        act.Should().Throw<InvalidOperationException>().WithMessage("*CacheHours*");
    }

    [Fact]
    public void ValidateAddressVerification_CacheHours720_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:CacheHours"] = "720" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateAddressVerification_NegativeCacheHours_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:CacheHours"] = "-1" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Development");
        act.Should().Throw<InvalidOperationException>().WithMessage("*CacheHours*");
    }

    // §233/contracts/cycle13/openapi.yaml (maxItems: 5) — review finding (cycle 13 review, non-blocking #6):
    // MaxCandidates previously wasn't validated at all, so a misconfigured value either broke the contract
    // (>5) or silently emptied every lookup result (Take(-1) for a non-positive value).
    [Fact]
    public void ValidateAddressVerification_MaxCandidatesAboveFive_ThrowsEvenInDevelopment()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:MaxCandidates"] = "50" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Development");
        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxCandidates*");
    }

    [Fact]
    public void ValidateAddressVerification_MaxCandidatesZeroOrNegative_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:MaxCandidates"] = "0" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Development");
        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxCandidates*");
    }

    [Fact]
    public void ValidateAddressVerification_MaxCandidatesInRange_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:MaxCandidates"] = "5" });
        var act = () => DeploymentSafetyChecks.ValidateAddressVerification(config, "Production");
        act.Should().NotThrow();
    }

    // §206/ARCHITECTURE_CYCLE13.md §420-422 — review finding (cycle 13 review, non-blocking #5): this
    // warning didn't exist at all, despite GeoOptions.StoreResults' own doc comment claiming it did.
    // A real provider must be configured for the warning to be meaningful (see the
    // "logging provider" test below for the companion review finding that this must NOT fire when
    // Provider=logging, since there is no geocoder call and therefore nothing to ever store).
    [Fact]
    public void ValidateAddressVerification_StoreResultsTrue_WithYandexProvider_WarnsAboutExtendedLicence()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AddressVerification:StoreResults"] = "true",
            ["AddressVerification:Provider"] = "yandex",
            ["AddressVerification:Yandex:ApiKey"] = "real-key",
        });
        var warnings = new List<string>();

        DeploymentSafetyChecks.ValidateAddressVerification(config, "Production", warn: warnings.Add);

        warnings.Should().ContainSingle(w => w.Contains("StoreResults") && w.Contains("licence"));
    }

    [Fact]
    public void ValidateAddressVerification_StoreResultsFalse_WithYandexProvider_DoesNotWarn()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AddressVerification:StoreResults"] = "false",
            ["AddressVerification:Provider"] = "yandex",
            ["AddressVerification:Yandex:ApiKey"] = "real-key",
        });
        var warnings = new List<string>();

        DeploymentSafetyChecks.ValidateAddressVerification(config, "Production", warn: warnings.Add);

        warnings.Should().BeEmpty();
    }

    // Review recheck (cycle 13, non-blocking backend finding): the StoreResults warning previously fired
    // even with the default Provider=logging, where there is no geocoder call and therefore no coordinates
    // that could ever be stored — a false "coordinates will be stored" warning on every dev/test host that
    // merely inherited StoreResults=true from shared config without a real provider enabled.
    [Fact]
    public void ValidateAddressVerification_StoreResultsTrue_WithLoggingProvider_DoesNotWarn()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["AddressVerification:StoreResults"] = "true" });
        var warnings = new List<string>();

        DeploymentSafetyChecks.ValidateAddressVerification(config, "Production", warn: warnings.Add);

        warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ValidatePhoneVerificationSecrets_MaxBotInDeveloperEnvironment_MissingSecrets_DoesNotThrow(string environmentName)
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["PhoneVerification:Provider"] = "max-bot" });
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(config, environmentName);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidatePhoneVerificationSecrets_MaxBotInProduction_MissingBotToken_Throws()
    {
        var values = ValidPhoneVerificationSecrets();
        values["PhoneVerification:Max:BotToken"] = "";
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*BotToken*");
    }

    [Fact]
    public void ValidatePhoneVerificationSecrets_MaxBotInProduction_MissingBotUsername_Throws()
    {
        var values = ValidPhoneVerificationSecrets();
        values["PhoneVerification:Max:BotUsername"] = "";
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*BotUsername*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void ValidatePhoneVerificationSecrets_MaxBotInProduction_WebhookTokenTooShort_Throws(string? webhookToken)
    {
        var values = ValidPhoneVerificationSecrets();
        values["PhoneVerification:Max:WebhookToken"] = webhookToken;
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*WebhookToken*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("http://ezbook.ru")] // О4: HTTPS only
    public void ValidatePhoneVerificationSecrets_MaxBotInProduction_InvalidPublicBaseUrl_Throws(string? publicBaseUrl)
    {
        var values = ValidPhoneVerificationSecrets();
        values["PhoneVerification:Max:PublicBaseUrl"] = publicBaseUrl;
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*PublicBaseUrl*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    public void ValidatePhoneVerificationSecrets_MaxBotInProduction_InvalidExternalKeyHmac_Throws(string? externalKey)
    {
        var values = ValidPhoneVerificationSecrets();
        values["PhoneVerification:ExternalKeyHmac"] = externalKey;
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*ExternalKeyHmac*");
    }

    [Fact]
    public void ValidatePhoneVerificationSecrets_MaxBotInProduction_ExternalKeyHmacWrongLength_Throws()
    {
        var values = ValidPhoneVerificationSecrets();
        values["PhoneVerification:ExternalKeyHmac"] = Convert.ToBase64String(new byte[16]); // 16, not 32 bytes
        var act = () => DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*ExternalKeyHmac*");
    }

    // ── ValidateVerificationMethodRegistry (ARCHITECTURE_CYCLE14.md §144.2) ────────────────────────

    [Fact]
    public void ValidateVerificationMethodRegistry_EveryMethodRegistered_DoesNotThrow()
    {
        var act = () => DeploymentSafetyChecks.ValidateVerificationMethodRegistry(
            Enum.GetValues<PhoneVerificationMethod>());
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateVerificationMethodRegistry_MissingAdapter_ThrowsNamingTheMissingMethod()
    {
        var act = () => DeploymentSafetyChecks.ValidateVerificationMethodRegistry([]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxBot*");
    }

    // ── ValidateTrialSecrets (ARCHITECTURE_CYCLE18.md §343.1) ───────────────────────────────────────

    private static Dictionary<string, string?> ValidTrialSecrets() => new()
    {
        ["Trial:UniquenessCheck:Enabled"] = "true",
        ["Trial:PhoneKeyHmac"] = Convert.ToBase64String(new byte[32]),
        ["Trial:PhoneKeyId"] = "2026-09",
    };

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ValidateTrialSecrets_DeveloperEnvironment_DoesNotThrow(string environmentName)
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(config, environmentName);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTrialSecrets_Production_ValidConfig_DoesNotThrow()
    {
        var config = BuildConfig(ValidTrialSecrets());
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(config, "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTrialSecrets_Production_UniquenessCheckDisabled_Throws()
    {
        var values = ValidTrialSecrets();
        values["Trial:UniquenessCheck:Enabled"] = "false";
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*UniquenessCheck*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    public void ValidateTrialSecrets_Production_InvalidPhoneKeyHmac_Throws(string? phoneKeyHmac)
    {
        var values = ValidTrialSecrets();
        values["Trial:PhoneKeyHmac"] = phoneKeyHmac;
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*PhoneKeyHmac*");
    }

    [Fact]
    public void ValidateTrialSecrets_Production_PhoneKeyHmacTooShort_Throws()
    {
        var values = ValidTrialSecrets();
        values["Trial:PhoneKeyHmac"] = Convert.ToBase64String(new byte[16]); // 16, not 32 bytes
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*PhoneKeyHmac*");
    }

    [Fact]
    public void ValidateTrialSecrets_Production_MissingPhoneKeyId_Throws()
    {
        var values = ValidTrialSecrets();
        values["Trial:PhoneKeyId"] = null;
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*PhoneKeyId*");
    }

    [Fact]
    public void ValidateTrialSecrets_Production_SameKeyAsPhoneVerificationExternalKey_Throws()
    {
        var sharedKey = Convert.ToBase64String(new byte[32]);
        var values = ValidTrialSecrets();
        values["Trial:PhoneKeyHmac"] = sharedKey;
        values["PhoneVerification:ExternalKeyHmac"] = sharedKey;
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*ExternalKeyHmac*");
    }

    [Fact]
    public void ValidateTrialSecrets_Production_SameKeyAsNotificationsEncryptionKey_Throws()
    {
        var sharedKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
        var values = ValidTrialSecrets();
        values["Trial:PhoneKeyHmac"] = sharedKey;
        values["Notifications:EncryptionKey"] = sharedKey;
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Notifications:EncryptionKey*");
    }

    [Fact]
    public void ValidateTrialSecrets_Production_DifferentExternalKey_DoesNotThrow()
    {
        var values = ValidTrialSecrets();
        values["PhoneVerification:ExternalKeyHmac"] = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray());
        values["Notifications:EncryptionKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)5, 32).ToArray());
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().NotThrow();
    }

    // N11 (code review, cycle 18 late delta) — TrialPhoneRegistrations.KeyId is string(16); a longer
    // Trial:PhoneKeyId would pass every other check above and then fail every trial activation's insert.
    [Fact]
    public void ValidateTrialSecrets_Production_PhoneKeyIdExactly16Chars_DoesNotThrow()
    {
        var values = ValidTrialSecrets();
        values["Trial:PhoneKeyId"] = new string('a', 16);
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTrialSecrets_Production_PhoneKeyIdTooLong_Throws()
    {
        var values = ValidTrialSecrets();
        values["Trial:PhoneKeyId"] = "2026-09-rotation-x"; // 19 chars — over the string(16) column
        var act = () => DeploymentSafetyChecks.ValidateTrialSecrets(BuildConfig(values), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*PhoneKeyId*");
    }
}
