using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

public class Booking
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ServiceId { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public string? ClientId { get; set; }

    // For guest bookings
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public string? GuestEmail { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    // Snapshot of Service.Price at the moment the booking was created, so that later price changes
    // on the service don't retroactively change historical revenue/commission figures.
    public decimal Price { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.NotRequired;
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public AppUser Master { get; set; } = null!;
    public AppUser? Client { get; set; }
}
