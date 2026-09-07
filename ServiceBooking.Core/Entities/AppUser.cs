using Microsoft.AspNetCore.Identity;

namespace ServiceBooking.Core.Entities;

public class AppUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompanyMember> CompanyMemberships { get; set; } = [];
    public ICollection<Booking> ClientBookings { get; set; } = [];
    public ICollection<Booking> MasterBookings { get; set; } = [];
    public ICollection<MasterService> MasterServices { get; set; } = [];
    public ICollection<WorkingHours> WorkingHours { get; set; } = [];
}
