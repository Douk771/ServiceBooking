namespace ServiceBooking.Core.Entities;

/// <summary>Journal of a channel's paid-period changes, by superadmin action (US-57 p.5) — same shape
/// as <see cref="SubscriptionChangeLog"/>.</summary>
public class ChannelPaymentLog
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }
    public NotificationChannel Channel { get; set; } = null!;

    public string ChangedByUserId { get; set; } = string.Empty;
    public DateTime? OldPaidUntil { get; set; }
    public DateTime? NewPaidUntil { get; set; }
    public decimal? Amount { get; set; }
    public string? Comment { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
