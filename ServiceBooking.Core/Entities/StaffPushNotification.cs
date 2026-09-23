using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// Outbox row for one Web Push notification to one subscribed device (ARCHITECTURE_CYCLE9.md §105.4,
/// §105.6, US-116). Exactly the same "queue entry, then permanent journal entry" shape as
/// <see cref="OutboundNotification"/> — rows are never deleted by the dispatch task, only their status
/// changes; retention (§105.11) is the only thing that ever removes a row.
///
/// One row = one subscription = one device: a master with three subscribed devices gets three rows per
/// event (§105.6 — "три устройства — три уведомления", by design).
/// </summary>
public class StaffPushNotification
{
    public Guid Id { get; set; }

    /// <summary>The recipient (a master). Right to send is re-checked against THIS user's current
    /// membership at send time, never trusted from queue time (§105.6, R4).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Company the booking belongs to — the one whose <c>StaffPushEnabled</c> setting and
    /// staff membership are re-checked at send time.</summary>
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>The specific device this row targets. Null only if the subscription was deleted between
    /// queueing and send (defensive — normally set).</summary>
    public Guid? SubscriptionId { get; set; }
    public PushSubscription? Subscription { get; set; }

    /// <summary>Always <see cref="NotificationType.StaffBookingCreated"/> this cycle (§105.6) — the enum
    /// member is reused, not newly minted, per §105.4's "оживает мёртвый член".</summary>
    public NotificationType Type { get; set; }

    /// <summary>Rendered body, snapshotted at queue time — a later booking edit must not change wording
    /// already queued (same rule as <see cref="OutboundNotification.Body"/>).</summary>
    public string Payload { get; set; } = string.Empty;

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public NotificationReason? Reason { get; set; }
    public string? ReasonDetail { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>In-flight marker, same convention as <see cref="OutboundNotification.LastAttemptAtUtc"/>.</summary>
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>§105.8 (Q17): <c>min(CreatedAt + 1h, VisitStartUtc)</c> — a row that outlives this
    /// instant becomes <see cref="NotificationStatus.Expired"/> and is never sent to the push service at
    /// all ("доехавшее позже уведомление вреднее недоехавшего").</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary><c>"{type}:{bookingId}:{userId}:{subscriptionId}"</c> — one row per (event, device),
    /// enforced by a unique index; a page refresh, a second tab on the same browser, or a dispatch retry
    /// all collide into the SAME row rather than creating a duplicate (§105.6).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
