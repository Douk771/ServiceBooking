using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §30.1, US-55 p.2 — every ProviderChannelState value, plus the
/// notAuthorized "still connecting" vs "regressed" fork.</summary>
public class ChannelStateMapperTests
{
    [Fact]
    public void Map_Authorized_ReturnsConnected()
    {
        var result = ChannelStateMapper.Map(ChannelState.Connecting, ProviderChannelState.Authorized, hadBeenConnected: false);
        result.State.Should().Be(ChannelState.Connected);
        result.Reason.Should().Be(ChannelStateReason.Authorized);
    }

    [Fact]
    public void Map_Blocked_ReturnsBlocked()
    {
        var result = ChannelStateMapper.Map(ChannelState.Connected, ProviderChannelState.Blocked, hadBeenConnected: true);
        result.State.Should().Be(ChannelState.Blocked);
        result.Reason.Should().Be(ChannelStateReason.ProviderReportsBlocked);
    }

    [Fact]
    public void Map_NotAuthorized_NeverConnectedBefore_StaysConnecting_NotARegression()
    {
        var result = ChannelStateMapper.Map(ChannelState.Connecting, ProviderChannelState.NotAuthorized, hadBeenConnected: false);
        result.State.Should().Be(ChannelState.Connecting);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Map_NotAuthorized_HadBeenConnectedBefore_IsDisconnected()
    {
        var result = ChannelStateMapper.Map(ChannelState.Connected, ProviderChannelState.NotAuthorized, hadBeenConnected: true);
        result.State.Should().Be(ChannelState.Disconnected);
        result.Reason.Should().Be(ChannelStateReason.ProviderReportsUnauthorized);
    }

    [Fact]
    public void Map_Starting_ReturnsConnecting()
    {
        var result = ChannelStateMapper.Map(ChannelState.NotConnected, ProviderChannelState.Starting, hadBeenConnected: false);
        result.State.Should().Be(ChannelState.Connecting);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Map_Unknown_NeverRegressesAConnectedChannel()
    {
        var result = ChannelStateMapper.Map(ChannelState.Connected, ProviderChannelState.Unknown, hadBeenConnected: true);
        result.State.Should().Be(ChannelState.Connected);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Map_Unknown_PreservesWhateverTheCurrentStateWas()
    {
        var result = ChannelStateMapper.Map(ChannelState.Disconnected, ProviderChannelState.Unknown, hadBeenConnected: true);
        result.State.Should().Be(ChannelState.Disconnected);
    }
}
