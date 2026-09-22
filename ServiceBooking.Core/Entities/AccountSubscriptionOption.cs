namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 5 (ARCHITECTURE_CYCLE5.md §43.3) — what's paid for on a billing account, beyond the base plan:
/// one row per (account, option). Today only the <c>notifications.whatsapp</c> row's
/// <see cref="Quantity"/> is read (§47.1's N, "how many notification numbers are paid for"); the
/// request/approval workflow fields below (US-70) are carried on the entity now so the admin-approval
/// endpoint — a later slice of this cycle — doesn't need its own migration, but nothing writes them yet.
/// </summary>
public class AccountSubscriptionOption
{
    public Guid Id { get; set; }

    public Guid BillingAccountId { get; set; }
    public BillingAccount BillingAccount { get; set; } = null!;

    public Guid OptionId { get; set; }
    public SubscriptionOption Option { get; set; } = null!;

    // >=1; always 1 for OptionKind.Toggle options (enforced by the admin write endpoint, not the DB —
    // same convention as SubscriptionOption.UnitName's Toggle/Quantity split).
    public int Quantity { get; set; } = 1;

    // null = "through the end of the account's own subscription period"; a value overrides that with
    // the option's own paid-through date.
    public DateTime? PaidUntilUtc { get; set; }

    // Set on disable/decrease: active through this date (SPEC §3.3 п. 9), then the row stops counting
    // but stays as history — never deleted.
    public DateTime? EndsAtUtc { get; set; }

    public DateTime? ActivatedAtUtc { get; set; }
    public string? ActivatedByUserId { get; set; }

    // US-70: a pending request to change Quantity, awaiting superadmin confirmation of payment.
    public int? RequestedQuantity { get; set; }
    public DateTime? RequestedAtUtc { get; set; }
    public string? RequestedByUserId { get; set; }
}
