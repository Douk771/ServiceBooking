using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §448.2, §456.2 — outbox row for one web-push to one order subscription: the shape of
/// <see cref="StaffPushNotification"/> without a user. Queue entry first, journal entry afterwards; only retention deletes rows.
/// </summary>
public class CustomerOrderPushNotification
{
    public Guid Id { get; set; }
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? SubscriptionId { get; set; }
    public OrderPushSubscription? Subscription { get; set; }

    public NotificationType Type { get; set; }

    /// <summary>Rendered body, snapshotted at queue time.</summary>
    public string Payload { get; set; } = string.Empty;

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public NotificationReason? Reason { get; set; }
    public string? ReasonDetail { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>Queue time + 2 h: a row that outlives it is never sent.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary><c>"{type}:{orderEventId}:{subscriptionId}"</c> — unique.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
