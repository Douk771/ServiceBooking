using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>A company's override of the platform default text for one notification type (US-59). No
/// row, or an empty <see cref="Body"/>, means "use the platform's default text".</summary>
public class NotificationTemplate
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public NotificationType Type { get; set; }
    public string Body { get; set; } = string.Empty; // max 1000

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
