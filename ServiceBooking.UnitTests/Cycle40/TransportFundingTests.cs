using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.3.2.</summary>
public class TransportFundingTests
{
    private static readonly DateTime Base = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TransportChannelFacts Ch(int minutes, ChannelState state = ChannelState.Connected, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), state, Base.AddMinutes(minutes));

    [Fact]
    public void Paid_FirstLiveChannelIsFunded_RestAreUnfunded()
    {
        var a = Ch(0); var b = Ch(5); var c = Ch(9);
        var rank = TransportFunding.Rank([c, a, b], transportPaid: true);
        rank[a.Id].Should().Be(ChannelFundingState.Funded);
        rank[b.Id].Should().Be(ChannelFundingState.Unfunded);
        rank[c.Id].Should().Be(ChannelFundingState.Unfunded);
    }

    [Fact]
    public void NotPaid_FirstLiveChannelIsNotPaid_RestUnfunded()
    {
        var a = Ch(0); var b = Ch(5);
        var rank = TransportFunding.Rank([a, b], transportPaid: false);
        rank[a.Id].Should().Be(ChannelFundingState.NotPaid);
        rank[b.Id].Should().Be(ChannelFundingState.Unfunded);
    }

    [Fact]
    public void ReplacedChannel_IsSkipped_AndAbsentFromResult()
    {
        var old = Ch(0, ChannelState.Replaced); var live = Ch(10);
        var rank = TransportFunding.Rank([old, live], transportPaid: true);
        rank.Should().NotContainKey(old.Id);
        rank[live.Id].Should().Be(ChannelFundingState.Funded);
    }

    [Fact]
    public void SameCreationTime_TiesBrokenById()
    {
        var low = Ch(0, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = Ch(0, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var rank = TransportFunding.Rank([high, low], transportPaid: true);
        rank[low.Id].Should().Be(ChannelFundingState.Funded);
        rank[high.Id].Should().Be(ChannelFundingState.Unfunded);
    }

    [Fact]
    public void NoChannels_EmptyResult() => TransportFunding.Rank([], true).Should().BeEmpty();
}
