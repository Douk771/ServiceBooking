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

    // Cycle 5 (ARCHITECTURE_CYCLE5.md §43.4) — the account this subscription belongs to; unique
    // (one subscription row per account, mirroring OwnerUserId's own uniqueness). OwnerUserId stays
    // as a history column read by reconciliation/incident tooling, but business logic reads
    // BillingAccountId from here on. Nullable for this slice — populated by a later
    // migration/provisioner, not backfilled here.
    public Guid? BillingAccountId { get; set; }
    public BillingAccount? BillingAccount { get; set; }

    public Guid? PlanConfigId { get; set; }
    public DateTime? PaidUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public AppUser Owner { get; set; } = null!;
    public SubscriptionPlanConfig? PlanConfig { get; set; }
}
