using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §497.2 — outbox row for one MAX message to one CHAT (not to a user or device).
/// The text carries no customer personal data.
/// </summary>
public class StaffMaxMessage
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Null for the owner's limit warning.</summary>
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }

    public string ChatKey { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Text { get; set; } = string.Empty;

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public NotificationReason? Reason { get; set; }
    public string? ReasonDetail { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>"{type}:{orderEventId}:{chatKey}" — one row per (event, chat).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
