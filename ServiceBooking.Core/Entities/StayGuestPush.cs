using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.6 — web-push subscription of a guest on the booking page (keys encrypted, AAD "stay-guest-push-subscription:{Id}").</summary>
public class StayGuestPushSubscription
{
    public Guid Id { get; set; }
    public Guid StayBookingId { get; set; }
    public StayBooking StayBooking { get; set; } = null!;
    public string Endpoint { get; set; } = string.Empty;
    public string P256dhCiphertext { get; set; } = string.Empty;
    public string AuthCiphertext { get; set; } = string.Empty;
    public string? KeyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSuccessAtUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
}

public class StayGuestPushNotification
{
    public Guid Id { get; set; }
    public Guid? StayBookingId { get; set; }
    public StayBooking? StayBooking { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public StayGuestPushSubscription? Subscription { get; set; }
    public NotificationType Type { get; set; }
    public string Payload { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public NotificationReason? Reason { get; set; }
    public string? ReasonDetail { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string IdempotencyKey { get; set; } = string.Empty;
}
