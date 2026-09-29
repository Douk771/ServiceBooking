using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §456.1 — create / update / remove of a browser subscription to ONE order's notifications (a customer without an account).
/// The keys are encrypted by <see cref="SecretProtector"/> against <c>"order-push-subscription:{Id}"</c> (the id is generated before insert, like
/// <see cref="PushSubscriptionWriter"/>). One order holds at most <c>Orders:MaxPushSubscriptionsPerOrder</c> subscriptions: past it the OLDEST is dropped silently.
/// The "count, then maybe evict" step runs under an advisory lock per order so two tabs subscribing at once cannot both slip past the ceiling.
/// </summary>
public sealed class OrderPushSubscriptionWriter(
    AppDbContext db, IOptions<NotificationOptions> notificationOptions, IOptions<OrdersOptions> ordersOptions)
{
    public async Task<(OrderPushSubscription Row, bool Created, int Count)> UpsertAsync(
        Guid orderId, string endpoint, string p256dh, string auth, CancellationToken ct)
    {
        var encryptionKey = notificationOptions.Value.EncryptionKey ?? string.Empty;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"order-push:{orderId}");

        var row = await db.OrderPushSubscriptions.FirstOrDefaultAsync(s => s.OrderId == orderId && s.Endpoint == endpoint, ct);
        var created = row is null;
        row ??= new OrderPushSubscription { Id = Guid.NewGuid(), OrderId = orderId, Endpoint = endpoint };
        if (created) db.OrderPushSubscriptions.Add(row);

        var aad = $"order-push-subscription:{row.Id}";
        row.P256dhCiphertext = SecretProtector.Encrypt(p256dh, encryptionKey, aad);
        row.AuthCiphertext = SecretProtector.Encrypt(auth, encryptionKey, aad);
        row.KeyId = SecretProtector.ComputeKeyId(SecretProtector.DecodeKey(encryptionKey));
        if (!created) row.ConsecutiveFailures = 0; // fresh keys: the browser re-subscribed

        await db.SaveChangesAsync(ct);

        var all = await db.OrderPushSubscriptions.Where(s => s.OrderId == orderId).ToListAsync(ct);
        var overflow = all.Count - ordersOptions.Value.MaxPushSubscriptionsPerOrder;
        if (overflow > 0)
        {
            db.OrderPushSubscriptions.RemoveRange(all.Where(s => s.Id != row.Id).OrderBy(s => s.CreatedAtUtc).Take(overflow));
            await db.SaveChangesAsync(ct);
        }
        var count = Math.Min(all.Count, ordersOptions.Value.MaxPushSubscriptionsPerOrder);
        await transaction.CommitAsync(ct);
        return (row, created, count);
    }

    /// <summary>Removes the row of this browser, if any — idempotent. Only the SERVER row: the browser's own subscription is not touched (goods shares one per origin, §456.3).</summary>
    public async Task RemoveAsync(Guid orderId, string endpoint, CancellationToken ct)
    {
        var row = await db.OrderPushSubscriptions.FirstOrDefaultAsync(s => s.OrderId == orderId && s.Endpoint == endpoint, ct);
        if (row is null) return;
        db.OrderPushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
