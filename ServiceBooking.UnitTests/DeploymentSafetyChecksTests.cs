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
}
