using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

// ARCHITECTURE_CYCLE10.md §102.1: append-only journal of what happened to a booking. Written exclusively
// by ServiceBooking.API.Services.Bookings.BookingEventLog — no controller adds a row directly.
public class BookingEvent
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }

    // Denormalized copy of Booking.CompanyId — same reasoning as ClientNotePhoto.CompanyId: the
    // history-access check and the retention scan both read this without a join to Bookings.
    public Guid CompanyId { get; set; }

    public BookingEventKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public BookingActorKind ActorKind { get; set; }
    public string? ActorUserId { get; set; }
    // Snapshot of the actor's name at the moment of the event — survives account/membership deletion
    // (US-124), same principle as Booking.Price/BookingService.NameSnapshot.
    public string? ActorNameSnapshot { get; set; }
    public UserRole? ActorRoleSnapshot { get; set; }

    // Filled only for Rescheduled.
    public DateOnly? PreviousDate { get; set; }
    public TimeOnly? PreviousStartTime { get; set; }
    public DateOnly? NewDate { get; set; }
    public TimeOnly? NewStartTime { get; set; }

    // Filled only for Cancelled, and only when a reason was supplied.
    public string? CancellationReason { get; set; }

    public Booking Booking { get; set; } = null!;
    public AppUser? ActorUser { get; set; }
}
