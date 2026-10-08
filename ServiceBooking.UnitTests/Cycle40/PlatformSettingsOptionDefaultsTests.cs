using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.7.1 (Р40-Ю1): the configuration only supplies the default of an ABSENT availability key — WhatsApp closed, MAX open.</summary>
public class PlatformSettingsOptionDefaultsTests
{
    private static PlatformSettings Settings(Dictionary<string, string?>? config = null) => new(
        null!, new MemoryCache(new MemoryCacheOptions()), new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build());

    [Fact]
    public void WithoutConfiguration_WhatsAppIsClosed_AndMaxIsOpen()
    {
        var settings = Settings();
        settings.OptionOpenDefault(NotificationTransport.WhatsApp).Should().BeFalse();
        settings.OptionOpenDefault(NotificationTransport.Max).Should().BeTrue();
    }

    [Fact]
    public void ConfigurationSection_OverridesTheDefaults()
    {
        var settings = Settings(new()
        {
            ["Notifications:OptionAvailability:WhatsApp"] = "true",
            ["Notifications:OptionAvailability:Max"] = "false",
        });
        settings.OptionOpenDefault(NotificationTransport.WhatsApp).Should().BeTrue();
        settings.OptionOpenDefault(NotificationTransport.Max).Should().BeFalse();
    }

    [Fact]
    public void TheShippedAppSettings_CloseWhatsApp_AndOpenMax()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        File.Exists(path).Should().BeTrue("the API's appsettings.json is copied next to the test assembly");
        var shipped = new ConfigurationBuilder().AddJsonFile(path).Build();
        var settings = new PlatformSettings(null!, new MemoryCache(new MemoryCacheOptions()), shipped);
        settings.OptionOpenDefault(NotificationTransport.WhatsApp).Should().BeFalse();
        settings.OptionOpenDefault(NotificationTransport.Max).Should().BeTrue();
    }

    [Fact]
    public void KeysAreThoseOfTheContract()
    {
        PlatformSettings.OptionOpenKey(NotificationTransport.WhatsApp).Should().Be("notifications.option.whatsapp.open");
        PlatformSettings.OptionOpenKey(NotificationTransport.Max).Should().Be("notifications.option.max.open");
        PlatformSettings.CustomerMessagingEnabledKey.Should().Be("notifications.customer-messaging.enabled");
        ChannelOptionAvailability.SettingKey(NotificationTransport.Max).Should().Be(PlatformSettings.OptionMaxOpenKey);
    }
}
