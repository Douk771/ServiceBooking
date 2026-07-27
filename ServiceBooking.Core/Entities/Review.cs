namespace ServiceBooking.Core.Entities;

public class Review
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid CompanyId { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string? ReviewerName { get; set; }
    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Booking Booking { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public AppUser Master { get; set; } = null!;
    public AppUser? Client { get; set; }
}
