namespace ServiceBooking.Core.Entities;

public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool AllowSelfBooking { get; set; } = true;
    public bool RequirePrepayment { get; set; } = false;
    // Owner's own opt-out from the public company directory (GET /api/companies), independent of the
    // tariff's AllowPublicListing flag — the company still exists and its own page is reachable by
    // slug, it just doesn't appear in the general listing.
    public bool ShowInPublicListing { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // The account holder (creator) — the subscription/tariff is bound to this user, and every company
    // they own (their "branches") shares that one account-level plan.
    public string OwnerUserId { get; set; } = string.Empty;
    public AppUser Owner { get; set; } = null!;

    public ICollection<CompanyMember> Members { get; set; } = [];
    public ICollection<Service> Services { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
}
