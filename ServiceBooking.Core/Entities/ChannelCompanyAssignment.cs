namespace ServiceBooking.Core.Entities;

/// <summary>
/// Assigns a company to a channel (US-61). A company may be assigned to at most one channel at a time —
/// enforced by a unique index on <see cref="CompanyId"/> (ARCHITECTURE_CYCLE4.md §23.1), not by
/// application-level checking, so the rule holds even under concurrent requests.
/// </summary>
public class ChannelCompanyAssignment
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }
    public NotificationChannel Channel { get; set; } = null!;

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // Cycle 5, stage 6 (ARCHITECTURE_CYCLE5.md §43.6) — the co-tenancy column: this assignment is
    // only valid while the company and the channel share this same billing account. Backed by two
    // composite FKs (see AppDbContext), which is what makes "assign a company to a number belonging
    // to a different account" physically impossible, and what forces CompanyTransferService to drop
    // the assignment before it can move the company to a different account.
    public Guid BillingAccountId { get; set; }

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public string AssignedByUserId { get; set; } = string.Empty;
}
