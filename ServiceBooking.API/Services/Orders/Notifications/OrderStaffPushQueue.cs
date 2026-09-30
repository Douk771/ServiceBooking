using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §455 — queues web-push rows for a shop's STAFF (and, for the monthly-limit warning, the account owner) on the
/// devices they subscribed from goods (<c>Site = Orders</c>). Reuses <see cref="StaffPushNotification"/> and its dispatch task: this class
/// only adds rows to the caller's transaction and never calls SaveChanges (the <c>StaffPushScheduler</c> convention). Rights are re-checked
/// by the dispatcher at SEND time, so a member removed after queueing gets nothing.
/// </summary>
public sealed class OrderStaffPushQueue(AppDbContext db, StaffPushLinks links)
{
    /// <summary>A queued row lives an hour: "a late new-order push does more harm than a missed one" (the salon rule, §105.8).</summary>
    public static readonly TimeSpan StaffPushTtl = TimeSpan.FromHours(1);

    /// <summary>One row per (staff member, device). The customer who is also staff of the shop is left out: they made the order themselves.</summary>
    public async Task QueueForStaffAsync(
        Order order, Guid orderEventId, NotificationType type, PushPayload payload, CancellationToken ct)
    {
        var members = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.CompanyId == order.CompanyId && cm.UserId != order.CustomerUserId)
            .Select(cm => cm.UserId).Distinct().ToListAsync(ct);
        await QueueAsync(members, order.CompanyId, order.Id, $"{type}:{orderEventId}", type, payload, ct);
    }

    /// <summary>The owner of the billing account is told that the monthly limit is near / reached (§459.6). <c>CompanyId</c> is the shop that crossed it.</summary>
    public Task QueueForOwnerAsync(
        Guid shopId, string ownerUserId, string keySeed, PushPayload payload, CancellationToken ct) =>
        QueueAsync([ownerUserId], shopId, null, keySeed, NotificationType.OwnerOrderLimitWarning, payload, ct);

    private async Task QueueAsync(
        IReadOnlyList<string> userIds, Guid companyId, Guid? orderId, string keySeed, NotificationType type, PushPayload payload,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        var subscriptions = await db.PushSubscriptions.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId)).ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        var now = DateTime.UtcNow;
        // API_CONTRACT_CYCLE33.md §33.26-27: every device of the recipient; the url is absolute for a subscription made on the other site.
        foreach (var subscription in subscriptions)
        {
            var body = StaffPushPayloadJson.Build(
                payload.Title, payload.Body, payload.Tag, links.ResolveUrl(subscription.Site, CompanyKind.Orders, payload.Url));
            db.StaffPushNotifications.Add(new StaffPushNotification
            {
                Id = Guid.NewGuid(),
                UserId = subscription.UserId,
                CompanyId = companyId,
                OrderId = orderId,
                SubscriptionId = subscription.Id,
                Type = type,
                Payload = body,
                Status = NotificationStatus.Pending,
                ExpiresAtUtc = now.Add(StaffPushTtl),
                CreatedAt = now,
                IdempotencyKey = $"{keySeed}:{subscription.UserId}:{subscription.Id}",
            });
        }
    }
}
