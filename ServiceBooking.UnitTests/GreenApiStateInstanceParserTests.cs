using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.GreenApi;

namespace ServiceBooking.UnitTests;

public class GreenApiStateInstanceParserTests
{
    [Theory]
    [InlineData("authorized", ProviderChannelState.Authorized)]
    [InlineData("notAuthorized", ProviderChannelState.NotAuthorized)]
    [InlineData("blocked", ProviderChannelState.Blocked)]
    [InlineData("starting", ProviderChannelState.Starting)]
    [InlineData("yellowCard", ProviderChannelState.Starting)]
    [InlineData("sleepMode", ProviderChannelState.Starting)]
    [InlineData("somethingUnrecognised", ProviderChannelState.Unknown)]
    [InlineData(null, ProviderChannelState.Unknown)]
    public void Parse_MapsEveryKnownRawValue(string? raw, ProviderChannelState expected)
    {
        GreenApiStateInstanceParser.Parse(raw).Should().Be(expected);
    }
}
