using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §499.1 — queues MAX messages for a shop's staff. Same conventions as <see cref="OrderStaffPushQueue"/>: rows are only ADDED to
/// the caller's transaction, SaveChanges is the caller's, no network calls. The addressee is a CHAT, not a user: one event yields one row per distinct
/// chat, however many accounts bound it (Q-25-3). Rights are re-checked by the dispatcher at SEND time.
/// </summary>
public sealed class OrderStaffMaxQueue(AppDbContext db)
{
    /// <summary>A queued row lives an hour — the push rule (§455): a late "new order" does more harm than a missed one.</summary>
    public static readonly TimeSpan MessageTtl = TimeSpan.FromHours(1);

    /// <summary>One row per active chat of the shop's staff. The customer who is also staff of the shop is left out: they made the order themselves.</summary>
    public async Task QueueForStaffAsync(Order order, Guid orderEventId, NotificationType type, string text, CancellationToken ct)
    {
        var userIds = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.CompanyId == order.CompanyId && cm.UserId != order.CustomerUserId)
            .Select(cm => cm.UserId).Distinct().ToListAsync(ct);
        await QueueAsync(userIds, order.CompanyId, order.Id, $"{type}:{orderEventId}", type, text, ct);
    }

    /// <summary>The owner of the billing account is told that the monthly limit is near / reached (P1). <c>CompanyId</c> is the shop that crossed it.</summary>
    public Task QueueForOwnerAsync(Guid shopId, string ownerUserId, string keySeed, string text, CancellationToken ct) =>
        QueueAsync([ownerUserId], shopId, null, keySeed, NotificationType.OwnerOrderLimitWarning, text, ct);

    private async Task QueueAsync(
        IReadOnlyList<string> userIds, Guid companyId, Guid? orderId, string keySeed, NotificationType type, string text, CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        var chatKeys = await db.StaffMaxLinks.AsNoTracking()
            .Where(l => userIds.Contains(l.UserId) && l.Status == StaffMaxLinkStatus.Active)
            .Select(l => l.ChatKey).Distinct().ToListAsync(ct);
        if (chatKeys.Count == 0) return;

        // A repeated queueing of the same event adds nothing (the unique key would refuse it anyway).
        var keys = chatKeys.ToDictionary(chatKey => chatKey, chatKey => $"{keySeed}:{chatKey}");
        var existing = await db.StaffMaxMessages.AsNoTracking().Where(m => keys.Values.Contains(m.IdempotencyKey))
            .Select(m => m.IdempotencyKey).ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var (chatKey, key) in keys)
        {
            if (existing.Contains(key)) continue;
            db.StaffMaxMessages.Add(new StaffMaxMessage
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                OrderId = orderId,
                ChatKey = chatKey,
                Type = type,
                Text = text,
                Status = NotificationStatus.Pending,
                ExpiresAtUtc = now.Add(MessageTtl),
                CreatedAt = now,
                IdempotencyKey = key,
            });
        }
    }
}
