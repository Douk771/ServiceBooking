using Microsoft.AspNetCore.Identity;

namespace ServiceBooking.Core.Entities;

public class AppUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Set once, by the user themselves, via POST /api/profile/delete-account (US-39, ARCHITECTURE.md
    // §5.3/§7.4). The row is kept as a tombstone rather than physically deleted — several FKs are
    // Restrict (Booking.Master, Review.Master, Company.Owner, MailLog.SentBy) and one is Cascade in a
    // way that would destroy company data if the row vanished (ClientNote.Master, notes about OTHER
    // clients written by this person). By the time this is set, every other personal-data field on this
    // row has already been scrubbed — a non-null value here is what distinguishes "scrubbed on purpose"
    // from data corruption.
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>ARCHITECTURE_CYCLE28.md §572.1 — the account was created by the showcase generator (owner, master or
    /// client of a fictional company). Such an account has no password and cannot log in on the production instance
    /// (§574.3). Everything else showcase-related is marked by ownership chain from the three root entities.</summary>
    public bool IsShowcase { get; set; }

    public ICollection<CompanyMember> CompanyMemberships { get; set; } = [];
    public ICollection<Booking> ClientBookings { get; set; } = [];
    public ICollection<Booking> MasterBookings { get; set; } = [];
    public ICollection<MasterService> MasterServices { get; set; } = [];
    public ICollection<WorkingHours> WorkingHours { get; set; } = [];
}
