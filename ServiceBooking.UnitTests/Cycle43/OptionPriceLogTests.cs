using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle43;

/// <summary>ARCHITECTURE_CYCLE43.md §43.2.1, §43.2.6 — the price journal covers exactly the two messenger options and only real changes.</summary>
public class OptionPriceLogTests
{
    [Theory]
    [InlineData("notifications.max", null, 490, true, true, true)]
    [InlineData("notifications.whatsapp", 390, null, true, true, true)]
    [InlineData("notifications.max", 490, 490, true, false, true)]
    [InlineData("notifications.max", 490, 490, true, true, false)]
    [InlineData("analytics", null, 490, true, true, false)]
    [InlineData("", null, 490, true, true, false)]
    public void ShouldWrite_OnlyForAMessengerOptionWithARealChange(string code, double? oldPrice, double? newPrice, bool oldActive, bool newActive, bool expected) =>
        OptionPriceLog.ShouldWrite(code, (decimal?)oldPrice, (decimal?)newPrice, oldActive, newActive).Should().Be(expected);

    [Fact]
    public void Write_ForANonMessengerOption_WritesNothing_AndNeverTouchesTheContext()
    {
        // db is null on purpose: the early return must happen before anything is added to the change tracker.
        var option = new SubscriptionOption { Id = Guid.NewGuid(), Code = "analytics" };
        OptionPriceLog.Write(null!, option, null, 100, true, true, OptionPriceChangeSource.OptionsCatalogApi, null, "admin", null).Should().BeFalse();
    }

    [Theory, InlineData("WhatsApp", true), InlineData("max", true), InlineData("MAX", true), InlineData(" Max ", true), InlineData("telegram", false),
        InlineData("0", false), InlineData("1", false), InlineData("", false), InlineData(null, false)]
    public void TransportRoute_AcceptsOnlyTheNamesOfTheTwoTransports(string? value, bool expected) =>
        MessengerTransportRoute.TryParse(value, out _).Should().Be(expected);
}
