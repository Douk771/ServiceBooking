namespace ServiceBooking.Core.Entities;

/// <summary>
/// US-67 (ARCHITECTURE_CYCLE6.md §44.2): one row per service in a visit, in the order the client
/// picked them. <see cref="Booking.ServiceId"/>/<see cref="Booking.Price"/> keep meaning "the first
/// service"/"the visit total" — this table is the itemised breakdown underneath that total, snapshot
/// at booking time so a later price change on the service never rewrites an already-created visit.
/// </summary>
public class BookingService
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid ServiceId { get; set; }

    /// <summary>0-based position within the visit — also the display order.</summary>
    public int Position { get; set; }

    /// <summary>Snapshot of Service.Name at booking time, so a later rename doesn't relabel history.</summary>
    public string NameSnapshot { get; set; } = string.Empty;

    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }

    public Booking Booking { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
