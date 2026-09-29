using ServiceBooking.Core.Enums;

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

    // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.4) — which billing account this row is about. Nullable:
    // every row written before this column existed (cycles 1-4) has none, and OwnerUserId above stays
    // their only key. New writes should populate it.
    public Guid? BillingAccountId { get; set; }
    public BillingAccount? BillingAccount { get; set; }

    // Populated only for ChangeKind == CompanyTransferred — which company moved.
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    // Defaults to Legacy (0) so every pre-cycle-5 row keeps an honest "kind unknown" value instead of
    // silently becoming e.g. "Plan" (§43.5).
    public SubscriptionChangeKind ChangeKind { get; set; } = SubscriptionChangeKind.Legacy;

    // Human-readable snapshot of the option set before/after the change ("WhatsApp ×1, Доп.
    // сотрудники ×3") — read by admins resolving a dispute, not reconstructed by code (§43.4).
    public string? OldOptionsSummary { get; set; }
    public string? NewOptionsSummary { get; set; }

    // ARCHITECTURE_CYCLE20.md §403.1 (US-20-02, migration M1, D3 п. 6.13.15.4). Populated only for a
    // manual assignment that ManualPlanAssignmentPolicy required a reason for, plus the trial regrant
    // path (TrialActivationService writes ReasonCode = TrialReissue). Both null for the ordinary paths
    // (owner self-service, the automatic trial→Free transition) — that is what "not a manual
    // assignment" looks like in this table, not a third enum member.
    public SubscriptionChangeReason? ReasonCode { get; set; }
    public string? ReasonDetails { get; set; }

    /// <summary>ARCHITECTURE_CYCLE24.md §459.5 — the tariff line the change is about (0 = "Записи", every old row).</summary>
    public CompanyKind Line { get; set; } = CompanyKind.Services;
}
