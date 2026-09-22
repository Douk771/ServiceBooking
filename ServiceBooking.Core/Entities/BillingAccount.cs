namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 7 (ARCHITECTURE_CYCLE7.md §43.2, §43.3) — the billing unit: "who pays and by which rules
/// companies live", as opposed to <see cref="Company.OwnerUserId"/> which answers "who manages/sees
/// it". An account is provisioned on demand (not at registration) — see
/// <c>BillingAccountProvisioner.EnsureAccountAsync</c>, a later slice of this cycle.
/// </summary>
public class BillingAccount
{
    public Guid Id { get; set; }

    // The account holder = payer. Unique — "one account per person", the same guarantee the
    // AccountSubscriptions.OwnerUserId unique index gives today.
    public string OwnerUserId { get; set; } = string.Empty;
    public AppUser Owner { get; set; } = null!;

    // Admin-only label ("Аккаунт Иванова"); never shown to the owner (SPEC §0.1 Р8).
    public string? Name { get; set; }

    // Extra employee seats granted by the cycle-5 migration when collapsing "per company" limits into
    // "summed per account" (§54.4). Not sold, not part of the amount due, visible only to admins.
    // Reset to 0 only by an admin action.
    public int GrandfatheredEmployeeBonus { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Cycle 7, stage 5 (ARCHITECTURE_CYCLE7.md §49, US-70) — the owner's single pending request to
    // change plan/options. Deliberately modeled as a handful of nullable columns on the account rather
    // than the separate SubscriptionRequest/SubscriptionRequestItem tables ARCHITECTURE_CYCLE7.md §43.3
    // sketches: "at most one pending request per account" then falls out of "these columns are either
    // all null or all set" instead of a partial unique index, and a resubmission is a plain overwrite
    // (contract requires this anyway — 200, not 201). This is a deliberate, reported deviation — see
    // the cycle-07 backend report; a later cycle may still want the richer relational shape (e.g. to
    // keep rejected/approved requests as history instead of just clearing these columns).
    public Guid? RequestedPlanId { get; set; }
    public SubscriptionPlanConfig? RequestedPlan { get; set; }

    // JSON-serialized List<RequestedOptionLine> (BillingAccountRequestExtensions) — the full desired
    // option set, exactly as submitted (SubscriptionRequestInput.options is a full set, not a delta).
    public string? RequestedOptionsJson { get; set; }

    public DateTime? RequestedAtUtc { get; set; }
    public string? RequestedByUserId { get; set; }
    public string? RequestedComment { get; set; }

    // N10, US-70 — the reason an admin gave for rejecting the owner's LAST request. Deliberately its
    // own pair of columns, not a reuse of RequestedComment (that field is the OWNER's own comment on
    // THEIR request, already cleared by the time a rejection reason exists — writing the admin's answer
    // into it would silently overwrite the owner's words with the admin's, and nothing reads it anyway
    // since RequestedAtUtc is null by the time this is set). Cleared the moment the owner submits a new
    // request (BillingController.SubmitRequest) or an admin approves one (AssignSubscription) — it is a
    // one-shot "here's why", not a running log (SubscriptionChangeLog is the append-only log; this is
    // just the thing to show once on the owner's own screen).
    public string? LastRejectionReason { get; set; }
    public DateTime? LastRejectedAtUtc { get; set; }
}

