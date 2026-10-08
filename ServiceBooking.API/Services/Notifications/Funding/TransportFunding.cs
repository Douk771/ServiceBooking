using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.Funding;

/// <summary>A live-or-replaced channel of ONE transport, reduced to what funding rank needs.</summary>
public readonly record struct TransportChannelFacts(Guid Id, ChannelState State, DateTime CreatedAt);

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.3.2 — replaces <c>ChannelFunding.Rank(channels, int)</c>: of the live channels
/// (<c>State ≠ Replaced</c>) of one transport, ordered by <c>CreatedAt, Id</c>, the first is
/// <see cref="ChannelFundingState.Funded"/> (transport paid) or <see cref="ChannelFundingState.NotPaid"/>; every
/// other one is <see cref="ChannelFundingState.Unfunded"/> (a surplus number). Pure.
/// </summary>
public static class TransportFunding
{
    public static IReadOnlyDictionary<Guid, ChannelFundingState> Rank(
        IEnumerable<TransportChannelFacts> transportChannels, bool transportPaid)
    {
        var live = transportChannels.Where(c => c.State != ChannelState.Replaced)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToList();

        var result = new Dictionary<Guid, ChannelFundingState>();
        for (var i = 0; i < live.Count; i++)
        {
            result[live[i].Id] = i == 0
                ? (transportPaid ? ChannelFundingState.Funded : ChannelFundingState.NotPaid)
                : ChannelFundingState.Unfunded;
        }
        return result;
    }
}
