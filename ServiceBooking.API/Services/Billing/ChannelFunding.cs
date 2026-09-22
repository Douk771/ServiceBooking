using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>Cycle 5 (ARCHITECTURE_CYCLE5.md §47.1) — the one place "is this number funded" gets
/// computed. <see cref="ChannelFundingState.Funded"/> is not "connected"/"working" in the transport
/// sense; it only answers "is the account's paid quantity enough to include this number".</summary>
public enum ChannelFundingState
{
    Funded,
    Unfunded,
    NotPaid,
}

public sealed record ChannelFundingResult(ChannelFundingState State, NotificationChannel? EarliestUnfundedBy);

public static class ChannelFunding
{
    /// <summary>
    /// Pure rule, no DB, unit-testable: an account with <paramref name="paidNumbers"/> paid numbers
    /// funds the <paramref name="paidNumbers"/> EARLIEST-created live channels
    /// (<c>CreatedAt ASC, Id ASC</c> — a stable order that never changes as channels reconnect/idle
    /// out, §47.1's rejected-alternatives table). Everything else is <see cref="ChannelFundingState.Unfunded"/>,
    /// or every channel is <see cref="ChannelFundingState.NotPaid"/> when <paramref name="paidNumbers"/>
    /// is 0. <c>Replaced</c> channels are excluded — a replaced number is dead history, not a live
    /// contender for the account's paid slots.
    /// </summary>
    public static IReadOnlyDictionary<Guid, ChannelFundingState> Rank(
        IReadOnlyList<NotificationChannel> accountChannels, int paidNumbers)
    {
        var live = accountChannels.Where(c => c.State != ChannelState.Replaced)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToList();

        var result = new Dictionary<Guid, ChannelFundingState>();
        if (paidNumbers <= 0)
        {
            foreach (var c in live) result[c.Id] = ChannelFundingState.NotPaid;
            return result;
        }

        for (var i = 0; i < live.Count; i++)
            result[live[i].Id] = i < paidNumbers ? ChannelFundingState.Funded : ChannelFundingState.Unfunded;

        return result;
    }
}
