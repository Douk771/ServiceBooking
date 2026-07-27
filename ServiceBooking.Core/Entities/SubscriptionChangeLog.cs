namespace ServiceBooking.Core.Entities;

public class SubscriptionChangeLog
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public Guid? OldPlanConfigId { get; set; }
    public Guid? NewPlanConfigId { get; set; }
    public DateTime? OldPaidUntil { get; set; }
    public DateTime? NewPaidUntil { get; set; }
    public bool OldIsActive { get; set; }
    public bool NewIsActive { get; set; }
    public string? Comment { get; set; }
}
