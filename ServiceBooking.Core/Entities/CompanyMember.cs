using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

public class CompanyMember
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? Bio { get; set; }
    // Commission is per-membership, not per-user: a master who moonlights at two companies can have a
    // different rate at each (US-15). Previously lived on AppUser, which meant one commission value
    // leaked across every company a moonlighting master belonged to.
    public decimal CommissionPercent { get; set; } = 0;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // US-62 (ARCHITECTURE_CYCLE6.md §40): whether this member shows up in the public "book a master"
    // list (GET /api/companies/{id}/masters). Meaningful for Master/CompanyOwner roles; defaults to
    // true so every existing membership (and the migration backfill) keeps today's visibility.
    public bool ProvidesServices { get; set; } = true;

    public Company Company { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}
