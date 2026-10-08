using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Shops;

/// <summary>A number assigned to a shop with its funding as the owner sees it.</summary>
public sealed record ShopChannelView(NotificationChannel Channel, ChannelFundingInfo Funding)
{
    public bool IsFunded => Funding.State == ChannelFundingState.Funded;
}

/// <summary>
/// ARCHITECTURE_CYCLE24.md §457.3, cycle 40 (§40.4): the numbers of a shop (or a "Дома" company) are those of its BILLING ACCOUNT —
/// one number serves every company of the account — with their funding (<see cref="ChannelFundingReader"/> — the ONE place "is this
/// number paid" is decided, so the shop screen, the dispatcher and the salon settings agree). <c>messengerAvailable</c> = a transport
/// is routable (paid, first number, not suspended, bound at least once) and the service is on.
/// </summary>
/// <remarks>Deviation from §40.4.2 #4: <see cref="IsMessengerAvailableAsync"/> stays until BE-40-5 introduces
/// <c>CustomerMessagingOffer</c> (the cached public offer) and moves its three consumers (<c>StorefrontController</c>,
/// <c>OrderCreationService</c>, <c>StayBookingCreationService</c>) to it; only its body changed to the account model.</remarks>
public sealed class ShopChannelReader(AccountMessagingReader messagingReader, ChannelFundingReader fundingReader)
{
    public async Task<List<ShopChannelView>> LoadAsync(Guid shopId, CancellationToken ct = default)
    {
        var messaging = await messagingReader.ForCompanyAsync(shopId, ct: ct);
        var channels = messaging.Channels.Where(c => c.State != ChannelState.Replaced).OrderBy(c => c.Transport).ThenBy(c => c.CreatedAt).ToList();
        if (channels.Count == 0) return [];
        var funding = await fundingReader.LoadAsync(channels, ct);
        return channels.Select(c => new ShopChannelView(
            c, funding.GetValueOrDefault(c.Id) ?? new ChannelFundingInfo(ChannelFundingState.NotPaid, string.Empty, null))).ToList();
    }

    public async Task<bool> IsMessengerAvailableAsync(Guid shopId, CancellationToken ct = default)
    {
        var messaging = await messagingReader.ForCompanyAsync(shopId, ct: ct);
        return messaging.PlatformEnabled && messaging.AnyRoutable;
    }
}
