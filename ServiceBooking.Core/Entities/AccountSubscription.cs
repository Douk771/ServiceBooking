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

    // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.4) — the account this subscription belongs to; unique
    // (one subscription row per account, mirroring OwnerUserId's own uniqueness). OwnerUserId stays
    // as a history column read by reconciliation/incident tooling (§54.5's before/after check), but
    // business logic reads BillingAccountId from here on. Stage 6 (§43.6, B5-13): NOT NULL at the
    // database level; CLR type stays `Guid?` for the same reason as Company.BillingAccountId.
    public Guid? BillingAccountId { get; set; }
    public BillingAccount? BillingAccount { get; set; }

    public Guid? PlanConfigId { get; set; }
    public DateTime? PaidUntil { get; set; }

    // Cycle 18 (ARCHITECTURE_CYCLE18.md §332.2, §333.2). Mailing capabilities of THIS subscription are
    // not good past this date — null means "through the end of the subscription itself", exactly the
    // semantics AccountSubscriptionOption.PaidUntilUtc already has (N12). Deliberately not named
    // TrialMailingUntil: this is a subscription property, not a trial marker, and SubscriptionResolver
    // applies it with one rule for any plan. The trial is its first consumer, not its only one.
    public DateTime? MailingUntilUtc { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public AppUser Owner { get; set; } = null!;
    public SubscriptionPlanConfig? PlanConfig { get; set; }
}
