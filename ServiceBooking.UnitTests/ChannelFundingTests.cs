using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE7.md §47.1 — "paid N, configured M" is a pure, deterministic rule: the
/// N earliest-created live channels are Funded, the rest Unfunded; N == 0 makes every live channel
/// NotPaid. No DB, no HTTP.</summary>
public class ChannelFundingTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static NotificationChannel Channel(int minutesAfterBase, Guid? id = null, ChannelState state = ChannelState.Connected) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CreatedAt = Base.AddMinutes(minutesAfterBase),
        State = state,
    };

    [Fact]
    public void Rank_PaidNumbersZero_EveryLiveChannelIsNotPaid()
    {
        var a = Channel(0);
        var b = Channel(10);

        var result = ChannelFunding.Rank([a, b], paidNumbers: 0);

        result[a.Id].Should().Be(ChannelFundingState.NotPaid);
        result[b.Id].Should().Be(ChannelFundingState.NotPaid);
    }

    [Fact]
    public void Rank_FewerChannelsThanPaid_AllFunded()
    {
        var a = Channel(0);
        var b = Channel(10);

        var result = ChannelFunding.Rank([a, b], paidNumbers: 5);

        result[a.Id].Should().Be(ChannelFundingState.Funded);
        result[b.Id].Should().Be(ChannelFundingState.Funded);
    }

    [Fact]
    public void Rank_MoreChannelsThanPaid_EarliestCreatedWin()
    {
        var oldest = Channel(0);
        var middle = Channel(10);
        var newest = Channel(20);

        var result = ChannelFunding.Rank([newest, oldest, middle], paidNumbers: 2);

        result[oldest.Id].Should().Be(ChannelFundingState.Funded);
        result[middle.Id].Should().Be(ChannelFundingState.Funded);
        result[newest.Id].Should().Be(ChannelFundingState.Unfunded);
    }

    [Fact]
    public void Rank_EqualCreatedAt_TieBrokenByIdAscending()
    {
        var lowerId = Channel(0, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var higherId = Channel(0, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));

        var result = ChannelFunding.Rank([higherId, lowerId], paidNumbers: 1);

        result[lowerId.Id].Should().Be(ChannelFundingState.Funded);
        result[higherId.Id].Should().Be(ChannelFundingState.Unfunded);
    }

    [Fact]
    public void Rank_ReplacedChannel_ExcludedFromRanking_DoesNotConsumeAPaidSlot()
    {
        var replaced = Channel(0, state: ChannelState.Replaced);
        var live = Channel(10);

        var result = ChannelFunding.Rank([replaced, live], paidNumbers: 1);

        result.Should().NotContainKey(replaced.Id);
        result[live.Id].Should().Be(ChannelFundingState.Funded);
    }

    [Fact]
    public void Rank_ReconnectingANumber_DoesNotChangeWhichNumbersAreFunded()
    {
        // §47.1's stability requirement: the rule keys off CreatedAt, never off connection state, so a
        // reconnect/idle cycle can't shuffle which numbers are paid.
        var oldest = Channel(0, state: ChannelState.NeedsReconnect);
        var newest = Channel(10, state: ChannelState.Connected);

        var result = ChannelFunding.Rank([oldest, newest], paidNumbers: 1);

        result[oldest.Id].Should().Be(ChannelFundingState.Funded);
        result[newest.Id].Should().Be(ChannelFundingState.Unfunded);
    }
}
