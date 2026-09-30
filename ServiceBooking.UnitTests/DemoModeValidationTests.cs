using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.2, lock 1 — <see cref="DeploymentSafetyChecks.ValidateDemoMode"/>: a production instance cannot be started as a demo by flipping one
/// switch. Pure: an in-memory configuration, no host.
/// </summary>
public class DemoModeValidationTests
{
    private static IConfiguration Config(Action<Dictionary<string, string?>>? change = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["DemoMode:Enabled"] = "true",
            ["PublicSites:ServicesBaseUrl"] = "https://demo.visit.ezbook.ru",
            ["AllowedOrigins"] = "https://demo.visit.ezbook.ru",
            ["ConnectionStrings:DefaultConnection"] = "Host=postgres-demo;Database=servicebooking_demo;Username=postgres;Password=x",
            ["Jwt:Issuer"] = "ServiceBooking.Demo",
            ["Notifications:Provider"] = "logging",
            ["Notifications:StaffPush:Provider"] = "logging",
            ["Notifications:StaffMax:Enabled"] = "false",
            ["PhoneVerification:Provider"] = "stub",
            ["Showcase:Reseed:Enabled"] = "false",
        };
        change?.Invoke(values);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void AGoodDemoConfiguration_Passes() =>
        ((Action)(() => DeploymentSafetyChecks.ValidateDemoMode(Config()))).Should().NotThrow();

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public void WhenDemoModeIsOff_NothingIsChecked_EvenForAProductionLookingConfiguration(string? enabled)
    {
        var production = Config(v =>
        {
            v["DemoMode:Enabled"] = enabled;
            v["PublicSites:ServicesBaseUrl"] = "https://ezbook.ru";
            v["AllowedOrigins"] = "https://ezbook.ru";
            v["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=servicebooking;Username=postgres;Password=x";
            v["Jwt:Issuer"] = "ServiceBooking";
            v["PhoneVerification:Provider"] = "max-bot";
        });

        ((Action)(() => DeploymentSafetyChecks.ValidateDemoMode(production))).Should().NotThrow();
    }

    [Theory]
    [InlineData("PublicSites:ServicesBaseUrl", "https://ezbook.ru", "PublicSites:ServicesBaseUrl")]
    [InlineData("PublicSites:ServicesBaseUrl", "https://visit.ezbook.ru", "PublicSites:ServicesBaseUrl")]
    [InlineData("PublicSites:ServicesBaseUrl", null, "PublicSites:ServicesBaseUrl")]
    [InlineData("AllowedOrigins", "https://demo.visit.ezbook.ru,https://ezbook.ru", "https://ezbook.ru")]
    [InlineData("AllowedOrigins", "http://localhost:5173", "localhost")]
    [InlineData("AllowedOrigins", "", "AllowedOrigins is empty")]
    [InlineData("ConnectionStrings:DefaultConnection", "Host=h;Database=servicebooking;Username=u;Password=p", "_demo")]
    [InlineData("ConnectionStrings:DefaultConnection", "Host=h;Database=servicebooking_demo_old;Username=u;Password=p", "_demo")]
    [InlineData("ConnectionStrings:DefaultConnection", null, "_demo")]
    [InlineData("Jwt:Issuer", "ServiceBooking", "Jwt:Issuer")]
    [InlineData("Jwt:Issuer", null, "Jwt:Issuer")]
    [InlineData("Notifications:Provider", "green-api", "Notifications:Provider")]
    [InlineData("Notifications:StaffPush:Provider", "web-push", "Notifications:StaffPush:Provider")]
    [InlineData("Notifications:StaffMax:Enabled", "true", "StaffMax")]
    [InlineData("PhoneVerification:Provider", "max-bot", "PhoneVerification:Provider")]
    [InlineData("Showcase:Reseed:Enabled", "true", "Showcase:Reseed:Enabled")]
    public void EachProductionSign_RefusesTheStart(string key, string? value, string expectedInMessage)
    {
        var config = Config(v => v[key] = value);

        var act = () => DeploymentSafetyChecks.ValidateDemoMode(config);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(expectedInMessage).And.Contain("DemoMode:Enabled is true");
    }

    [Fact]
    public void AllProblemsAreReportedTogether()
    {
        var config = Config(v =>
        {
            v["PublicSites:ServicesBaseUrl"] = "https://ezbook.ru";
            v["Jwt:Issuer"] = "ServiceBooking";
            v["PhoneVerification:Provider"] = "max-bot";
        });

        var act = () => DeploymentSafetyChecks.ValidateDemoMode(config);

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("PublicSites:ServicesBaseUrl").And.Contain("Jwt:Issuer").And.Contain("PhoneVerification:Provider");
    }

    [Theory]
    [InlineData("Host=h;Database=servicebooking_DEMO;Username=u;Password=p")]
    [InlineData("Host=h;Database=sbtest_0a1b2c3d_demo;Username=u;Password=p")]
    public void DatabaseNameSuffix_IsCaseInsensitive_AndAcceptsTheTestDatabaseSlotNamedDemo(string connectionString) =>
        ((Action)(() => DeploymentSafetyChecks.ValidateDemoMode(Config(v => v["ConnectionStrings:DefaultConnection"] = connectionString)))).Should().NotThrow();

    [Fact]
    public void AMalformedConnectionString_IsAProblem_NotACrash()
    {
        var config = Config(v => v["ConnectionStrings:DefaultConnection"] = "this is not a connection string");

        var act = () => DeploymentSafetyChecks.ValidateDemoMode(config);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("_demo");
    }

    [Fact]
    public void WithoutExplicitProviders_TheRepositoryDefaultsAreAccepted_LoggingAndStub()
    {
        var config = Config(v =>
        {
            v.Remove("Notifications:Provider");
            v.Remove("Notifications:StaffPush:Provider");
            v.Remove("PhoneVerification:Provider");
        });

        ((Action)(() => DeploymentSafetyChecks.ValidateDemoMode(config))).Should().NotThrow();
    }
}
