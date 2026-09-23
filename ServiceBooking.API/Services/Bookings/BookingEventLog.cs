using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Bookings;

/// <summary>
/// ARCHITECTURE_CYCLE10.md §105: the ONLY place in the codebase that writes to BookingEvents — no
/// controller ever calls db.BookingEvents.Add(...) directly.
///
/// Deliberately does NOT call SaveChangesAsync. It only adds the new row to the currently tracked
/// graph; the caller's own SaveChangesAsync (already in flight for the actual booking change) is what
/// persists both together. EF wraps a single SaveChangesAsync in one transaction, so "the booking
/// changed but the journal didn't" and "the journal has a row but the booking didn't change" are both
/// impossible by construction — not by every call site remembering to be careful.
///
/// ⚠️ Callers must NOT wrap Append in try/catch. Unlike NotificationScheduler (which is deliberately
/// fire-and-forget so a notification outage can't block a booking), a failure to record a booking event
/// must fail the whole operation — US-123 requires the journal to be an accurate record of what
/// happened, and a silently-swallowed failure here would produce exactly the "change happened, no
/// journal row" state the append-only design exists to prevent.
/// </summary>
public class BookingEventLog(AppDbContext db)
{
    public void Append(
        Booking booking,
        BookingEventKind kind,
        BookingActorKind actorKind,
        string? actorUserId,
        string? actorNameSnapshot,
        UserRole? actorRoleSnapshot,
        DateOnly? previousDate = null,
        TimeOnly? previousStartTime = null,
        DateOnly? newDate = null,
        TimeOnly? newStartTime = null,
        string? cancellationReason = null)
    {
        db.BookingEvents.Add(new BookingEvent
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            CompanyId = booking.CompanyId,
            Kind = kind,
            OccurredAtUtc = DateTime.UtcNow,
            ActorKind = actorKind,
            ActorUserId = actorUserId,
            ActorNameSnapshot = actorNameSnapshot,
            ActorRoleSnapshot = actorRoleSnapshot,
            PreviousDate = previousDate,
            PreviousStartTime = previousStartTime,
            NewDate = newDate,
            NewStartTime = newStartTime,
            CancellationReason = cancellationReason,
        });
    }
}
