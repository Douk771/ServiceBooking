using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.7.4 — the expected server country is per transport, with the common value as the fallback.</summary>
public class ServerCountryByTransportTests
{
    [Fact]
    public void OwnEntryWins_ElseTheCommonValue()
    {
        var options = new NotificationOptions.GreenApiOptions { ServerCountry = "RU" };
        options.ServerCountryByTransport["Max"] = "KZ";

        options.ExpectedServerCountry(NotificationTransport.Max).Should().Be("KZ");
        options.ExpectedServerCountry(NotificationTransport.WhatsApp).Should().Be("RU");
    }

    [Fact]
    public void NothingConfigured_IsEmpty() =>
        new NotificationOptions.GreenApiOptions().ExpectedServerCountry(NotificationTransport.Max).Should().BeEmpty();

    [Fact]
    public void BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:GreenApi:ServerCountry"] = "RU",
            ["Notifications:GreenApi:ServerCountryByTransport:Max"] = "BY",
        }).Build();
        var options = config.GetSection("Notifications").Get<NotificationOptions>()!;

        options.GreenApi.ExpectedServerCountry(NotificationTransport.Max).Should().Be("BY");
        options.GreenApi.ExpectedServerCountry(NotificationTransport.WhatsApp).Should().Be("RU");
        options.TestMessage.AllowSameNumber.Should().BeFalse("the default is off until MAX is checked on a real number");
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public void Startup_BothTransportsNeedACountry_WhenCreationIsOn()
    {
        var enabled = ("Notifications:GreenApi:InstanceCreationEnabled", "true");

        var none = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(Config(enabled));
        none.Should().Throw<InvalidOperationException>().WithMessage("*ServerCountry*");

        var onlyMax = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(
            Config(enabled, ("Notifications:GreenApi:ServerCountryByTransport:Max", "RU")));
        onlyMax.Should().Throw<InvalidOperationException>().WithMessage("*WhatsApp*", "WhatsApp has neither its own nor the common country");

        var both = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(Config(
            enabled, ("Notifications:GreenApi:ServerCountryByTransport:Max", "RU"), ("Notifications:GreenApi:ServerCountryByTransport:WhatsApp", "RU")));
        both.Should().NotThrow();

        var common = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(Config(enabled, ("Notifications:GreenApi:ServerCountry", "RU")));
        common.Should().NotThrow();

        var off = () => DeploymentSafetyChecks.ValidateGreenApiServerCountry(Config());
        off.Should().NotThrow("creation is off by default");
    }
}
