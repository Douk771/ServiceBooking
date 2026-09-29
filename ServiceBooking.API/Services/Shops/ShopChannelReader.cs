using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>A number assigned to a shop with its funding as the owner sees it.</summary>
public sealed record ShopChannelView(NotificationChannel Channel, ChannelFundingInfo Funding)
{
    public bool IsFunded => Funding.State == ChannelFundingState.Funded;
}

/// <summary>
/// ARCHITECTURE_CYCLE24.md §457.3 — the numbers assigned to a shop and whether they are paid (<see cref="ChannelFundingReader"/> — the ONE place
/// "is this number paid" is decided, so the shop screen, the dispatcher and the salon settings agree). <c>messengerAvailable</c> = an assigned,
/// paid number exists.
/// </summary>
public sealed class ShopChannelReader(AppDbContext db, ChannelFundingReader fundingReader)
{
    public async Task<List<ShopChannelView>> LoadAsync(Guid shopId, CancellationToken ct = default)
    {
        var channels = await db.ChannelCompanyAssignments.AsNoTracking().Include(a => a.Channel)
            .Where(a => a.CompanyId == shopId).OrderBy(a => a.Transport).Select(a => a.Channel).ToListAsync(ct);
        if (channels.Count == 0) return [];
        var funding = await fundingReader.LoadAsync(channels, ct);
        return channels.Select(c => new ShopChannelView(
            c, funding.GetValueOrDefault(c.Id) ?? new ChannelFundingInfo(ChannelFundingState.NotPaid, string.Empty, null))).ToList();
    }

    public async Task<bool> IsMessengerAvailableAsync(Guid shopId, CancellationToken ct = default) =>
        (await LoadAsync(shopId, ct)).Any(v => v.IsFunded);
}
