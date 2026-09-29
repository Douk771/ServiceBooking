namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §448.2, §449.4 — a date on which a shop's hours differ from its weekly schedule (a holiday, a short
/// day). PK (CompanyId, Date). Past rows are kept (history for cycle 3) but only rows inside the 90-day horizon are read.
/// </summary>
public class ShopSpecialDay
{
    public Guid CompanyId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsClosed { get; set; }

    /// <summary>Canonical intervals JSON (§449.1); null when <see cref="IsClosed"/>.</summary>
    public string? IntervalsJson { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
