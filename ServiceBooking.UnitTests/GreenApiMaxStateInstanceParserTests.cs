using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.GreenApiMax;

namespace ServiceBooking.UnitTests;

public class GreenApiMaxStateInstanceParserTests
{
    [Theory]
    [InlineData("authorized", ProviderChannelState.Authorized)]
    [InlineData("notAuthorized", ProviderChannelState.NotAuthorized)]
    [InlineData("blocked", ProviderChannelState.Blocked)]
    [InlineData("starting", ProviderChannelState.Starting)]
    // B1 (ARCHITECTURE_CYCLE9.md §104.9): MAX-only values, mapped consciously — see the class's own doc
    // comment for why each lands where it does.
    [InlineData("pendingPassword", ProviderChannelState.NotAuthorized)]
    [InlineData("suspended", ProviderChannelState.Unknown)]
    [InlineData("somethingUnrecognised", ProviderChannelState.Unknown)]
    [InlineData(null, ProviderChannelState.Unknown)]
    public void Parse_MapsEveryKnownRawValue(string? raw, ProviderChannelState expected)
    {
        GreenApiMaxStateInstanceParser.Parse(raw).Should().Be(expected);
    }
}
