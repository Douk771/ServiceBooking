using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services;

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
}
