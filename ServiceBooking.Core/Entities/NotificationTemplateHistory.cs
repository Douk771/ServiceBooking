using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>Snapshot of a template's PREVIOUS text every time it changes (US-59 p.8) — lets an owner see
/// what a message used to say, independent of <see cref="NotificationTemplate"/>'s current row.</summary>
public class NotificationTemplateHistory
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public NotificationType Type { get; set; }
    public string PreviousBody { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
