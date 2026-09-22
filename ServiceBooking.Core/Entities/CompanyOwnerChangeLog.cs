namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 5 (ARCHITECTURE_CYCLE5.md §43.3) — audit trail for changing who manages a company
/// (US-64 p.4), independent of <see cref="SubscriptionChangeLog"/>: a stand-alone change of
/// responsible owner writes nothing to the subscription log — that's the whole point (money isn't
/// touched). <see cref="WithTransfer"/> distinguishes that case from an owner change that happens as
/// part of moving the company to another billing account (§51.3), which is a later slice of this
/// cycle.
/// </summary>
public class CompanyOwnerChangeLog
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string OldOwnerUserId { get; set; } = string.Empty;
    public string NewOwnerUserId { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Comment { get; set; }

    // true only when the owner change happened as a side effect of a company transfer between
    // billing accounts (§51.3); false (default) for a stand-alone change of responsible owner.
    public bool WithTransfer { get; set; }
}
