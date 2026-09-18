using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// One row is both a queue entry and, once it leaves <see cref="NotificationStatus.Pending"/>, a
/// permanent journal entry (ARCHITECTURE_CYCLE4.md §23.4) — rows are never deleted, only their status
/// changes. See <see cref="NotificationStatus"/> for why <c>Pending</c> must stay the enum's 0 value.
/// </summary>
public class OutboundNotification
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // Null only for Skipped rows queued with no usable channel at all (§23.4).
    public Guid? ChannelId { get; set; }
    public NotificationChannel? Channel { get; set; }

    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public NotificationType Type { get; set; }

    public string RecipientPhone { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public string? RecipientUserId { get; set; }

    // Rendered text, snapshotted at queue time (US-59 p.7) — a later template edit must not change
    // wording already queued.
    public string Body { get; set; } = string.Empty;

    public DateTime DueAtUtc { get; set; }

    // Denormalized visit time — see NotificationTiming and §23.4 for why this is kept in sync with the
    // booking rather than joined for every read.
    public DateTime VisitStartUtc { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public NotificationReason? Reason { get; set; }
    public string? ReasonDetail { get; set; }

    public int AttemptCount { get; set; }

    // Doubles as the "in flight" marker (§26.3): written BEFORE the outbound HTTP call, so a concurrent
    // or restarted pass can tell a row is currently being sent and skip it for InFlightGraceMinutes.
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }

    public string? ProviderMessageId { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    // Booking reschedule generation (§23.5) — part of the idempotency key so a reschedule can queue a
    // fresh reminder without colliding with the one it supersedes.
    public int Generation { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
