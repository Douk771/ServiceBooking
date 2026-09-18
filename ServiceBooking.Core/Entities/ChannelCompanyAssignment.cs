namespace ServiceBooking.Core.Entities;

/// <summary>
/// Assigns a company to a channel (US-61). A company may be assigned to at most one channel at a time —
/// enforced by a unique index on <see cref="CompanyId"/> (ARCHITECTURE_CYCLE4.md §23.1), not by
/// application-level checking, so the rule holds even under concurrent requests.
/// </summary>
public class ChannelCompanyAssignment
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }
    public NotificationChannel Channel { get; set; } = null!;

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public string AssignedByUserId { get; set; } = string.Empty;
}
