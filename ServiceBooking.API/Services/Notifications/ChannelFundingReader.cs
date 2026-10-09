using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>A channel's funding as the owner sees it: the §40.3.2 rank, its user-facing text, and the
/// paid-until date shown for it (the payment of the channel's own transport).</summary>
public sealed record ChannelFundingInfo(ChannelFundingState State, string Text, DateTime? PaidUntil);

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.3.3 — the façade of the former per-channel funding reader, now a thin layer over
/// <see cref="AccountMessagingReader"/>: a channel is funded when it is the first live number of a transport that is paid
/// (<see cref="ChannelOptionFunding"/>), any further number of the transport is "unfunded", a transport that is not paid is
/// "not paid". No tariff flag and no paid-numbers count takes part.
/// </summary>
public sealed class ChannelFundingReader(AccountMessagingReader messagingReader)
{
    /// <summary>Funding for every channel of every billing account the given channels belong to (a channel without a billing
    /// account gets no entry — callers treat that as NotPaid). <paramref name="nowUtc"/> is "now" of the payment rule; defaults
    /// to the wall clock (scheduled tasks pass their own clock instant).</summary>
    public async Task<Dictionary<Guid, ChannelFundingInfo>> LoadAsync(
        IReadOnlyList<NotificationChannel> channels, CancellationToken ct = default, DateTime? nowUtc = null)
    {
        var result = new Dictionary<Guid, ChannelFundingInfo>();
        var accountIds = channels.Where(c => c.BillingAccountId.HasValue).Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        if (accountIds.Count == 0) return result;

        var states = await messagingReader.LoadAsync(accountIds, nowUtc, ct);
        foreach (var accountId in accountIds)
        {
            if (!states.TryGetValue(accountId, out var account)) continue;
            foreach (var channel in account.Channels)
            {
                var transport = account.For(channel.Transport);
                var state = account.FundingOf(channel);
                var workingMasked = transport.Primary?.PhoneNumber is { } phone ? PhoneDisplayMask.Mask(phone) : null;
                var text = MessengerTexts.FundingText(
                    state, channel.Transport, new TransportPaymentView(transport.Payment.PaidUntil, transport.Payment.IsTrial), workingMasked);
                result[channel.Id] = new ChannelFundingInfo(state, text, transport.Payment.PaidUntil);
            }
        }
        return result;
    }

    /// <summary>Is this one channel the funded first number of its paid transport? No channel, or one without a billing
    /// account, never is.</summary>
    public async Task<bool> IsFundedAsync(NotificationChannel? channel, CancellationToken ct = default)
    {
        if (channel?.BillingAccountId is not { } accountId) return false;
        var account = await messagingReader.ForAccountAsync(accountId, ct: ct);
        return account is not null && account.FundingOf(channel) == ChannelFundingState.Funded;
    }
}
