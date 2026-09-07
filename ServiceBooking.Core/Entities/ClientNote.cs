namespace ServiceBooking.Core.Entities;

public class ClientNote
{
    public Guid Id { get; set; }
    // The company the note was written in. Notes are shared across all masters of this company for the
    // same client (so a master seeing a new booking sees prior notes from colleagues), but NOT across
    // different companies. MasterId still records the author, who — together with the company's owner
    // (decision Q16) — alone may delete it.
    public Guid CompanyId { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string? GuestPhone { get; set; }
    public string Note { get; set; } = string.Empty;
    // The visit this note was written about, when it was written from the panel under a booking.
    // Optional on purpose: a note can also be filed straight from the client card, outside any visit.
    // SetNull rather than Cascade — deleting a booking (which the product does not do today; bookings
    // are only cancelled) must not take the note and its photos with it, since the work was still done
    // (US-20 p.5).
    public Guid? BookingId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Company Company { get; set; } = null!;
    public AppUser Master { get; set; } = null!;
    public AppUser? Client { get; set; }
    public Booking? Booking { get; set; }
    public ICollection<ClientNotePhoto> Photos { get; set; } = [];
}
