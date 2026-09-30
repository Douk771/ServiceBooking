using ServiceBooking.API.Services.Notifications;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §456.2 — queues one <see cref="CustomerOrderPushNotification"/> per browser subscription of the order
/// (a customer without an account). Rows are added to the caller's transaction; SaveChanges is the caller's. The push body carries no name
/// and no phone [legal L10]; the order link (with the secret token) travels only inside the encrypted payload.
/// </summary>
public sealed class CustomerOrderPushQueue(AppDbContext db)
{
    /// <summary>A queued row lives two hours (the customer's status stays useful longer than staff's).</summary>
    public static readonly TimeSpan CustomerPushTtl = TimeSpan.FromHours(2);

    public async Task QueueAsync(Order order, Guid orderEventId, NotificationType type, PushPayload payload, CancellationToken ct)
    {
        var subscriptions = await db.OrderPushSubscriptions.AsNoTracking().Where(s => s.OrderId == order.Id).ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        var now = DateTime.UtcNow;
        var body = JsonSerializer.Serialize(new { title = payload.Title, body = payload.Body, tag = payload.Tag, url = payload.Url }, StaffPushPayloadJson.Options);
        foreach (var subscription in subscriptions)
            db.CustomerOrderPushNotifications.Add(new CustomerOrderPushNotification
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                CompanyId = order.CompanyId,
                SubscriptionId = subscription.Id,
                Type = type,
                Payload = body,
                Status = NotificationStatus.Pending,
                ExpiresAtUtc = now.Add(CustomerPushTtl),
                CreatedAt = now,
                IdempotencyKey = $"{type}:{orderEventId}:{subscription.Id}",
            });
    }
}
