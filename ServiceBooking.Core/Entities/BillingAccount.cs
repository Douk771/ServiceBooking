namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 5 (ARCHITECTURE_CYCLE5.md §43.2, §43.3) — the billing unit: "who pays and by which rules
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
}
