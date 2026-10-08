using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.2.6, API_CONTRACT_CYCLE37.md §37.26.5 — upsert by (owner, endpoint), keys encrypted with AAD "stay-guest-push-subscription:{Id}", at most 5 per owner
/// (the oldest is pushed out). The owner is a house booking or (cycle 39, §39.2.2) a stand-alone order of a service.
/// </summary>
public sealed class StayGuestPushSubscriptionWriter(AppDbContext db, IOptions<NotificationOptions> notificationOptions, IOptions<StaysOptions> options)
{
    public Task<(StayGuestPushSubscription Row, bool Created)> UpsertAsync(Guid bookingId, string endpoint, string p256dh, string auth, CancellationToken ct) =>
        UpsertCoreAsync(bookingId, orderId: null, endpoint, p256dh, auth, ct);

    public Task<(StayGuestPushSubscription Row, bool Created)> UpsertForOrderAsync(Guid orderId, string endpoint, string p256dh, string auth, CancellationToken ct) =>
        UpsertCoreAsync(bookingId: null, orderId, endpoint, p256dh, auth, ct);

    private async Task<(StayGuestPushSubscription Row, bool Created)> UpsertCoreAsync(Guid? bookingId, Guid? orderId, string endpoint, string p256dh, string auth, CancellationToken ct)
    {
        var encryptionKey = notificationOptions.Value.EncryptionKey ?? string.Empty;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"stay-push:{bookingId ?? orderId}");

        IQueryable<StayGuestPushSubscription> Owned() => bookingId is { } b
            ? db.StayGuestPushSubscriptions.Where(s => s.StayBookingId == b) : db.StayGuestPushSubscriptions.Where(s => s.StayServiceOrderId == orderId);

        var row = await Owned().FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        var created = row is null;
        row ??= new StayGuestPushSubscription { Id = Guid.NewGuid(), StayBookingId = bookingId, StayServiceOrderId = orderId, Endpoint = endpoint };
        if (created) db.StayGuestPushSubscriptions.Add(row);
        var aad = $"stay-guest-push-subscription:{row.Id}";
        row.P256dhCiphertext = SecretProtector.Encrypt(p256dh, encryptionKey, aad);
        row.AuthCiphertext = SecretProtector.Encrypt(auth, encryptionKey, aad);
        row.KeyId = SecretProtector.ComputeKeyId(SecretProtector.DecodeKey(encryptionKey));
        if (!created) row.ConsecutiveFailures = 0;
        await db.SaveChangesAsync(ct);

        var all = await Owned().ToListAsync(ct);
        var overflow = all.Count - options.Value.PushSubscriptionsPerBooking;
        if (overflow > 0)
        {
            db.StayGuestPushSubscriptions.RemoveRange(all.Where(s => s.Id != row.Id).OrderBy(s => s.CreatedAtUtc).Take(overflow));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return (row, created);
    }

    public async Task RemoveAsync(Guid bookingId, string endpoint, CancellationToken ct)
    {
        var row = await db.StayGuestPushSubscriptions.FirstOrDefaultAsync(s => s.StayBookingId == bookingId && s.Endpoint == endpoint, ct);
        if (row is null) return;
        db.StayGuestPushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveForOrderAsync(Guid orderId, string endpoint, CancellationToken ct)
    {
        var row = await db.StayGuestPushSubscriptions.FirstOrDefaultAsync(s => s.StayServiceOrderId == orderId && s.Endpoint == endpoint, ct);
        if (row is null) return;
        db.StayGuestPushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
