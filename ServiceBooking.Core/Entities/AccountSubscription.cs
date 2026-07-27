namespace ServiceBooking.Core.Entities;

/// <summary>
/// A subscription bound to an account (the owner/creator user), not to a single company. One plan
/// governs all of the owner's companies ("branches"): their feature flags, employee-per-company limit
/// and branch-count limit.
/// </summary>
public class AccountSubscription
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid? PlanConfigId { get; set; }
    public DateTime? PaidUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public AppUser Owner { get; set; } = null!;
    public SubscriptionPlanConfig? PlanConfig { get; set; }
}
