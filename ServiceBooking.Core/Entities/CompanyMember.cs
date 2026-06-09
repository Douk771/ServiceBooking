using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

public class CompanyMember
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? Bio { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}
