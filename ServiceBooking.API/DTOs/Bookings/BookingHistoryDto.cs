using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Bookings;

// ARCHITECTURE_CYCLE10.md §122: staff-only, never reaches a client DTO — see BookingDto.HistoryEventCount
// for the analogous "not filled, not hidden on the frontend" rule (П8).

public record BookingEventActorDto(
    BookingActorKind Kind,
    // Snapshot of the actor's name at the moment of the event; null only for System.
    string? Name,
    // Role in THIS company at the moment of the event; null for every actor kind except Staff.
    UserRole? Role,
    string Label
);

public record BookingRescheduleDto(DateOnly FromDate, TimeOnly FromStartTime, DateOnly ToDate, TimeOnly ToStartTime);

public record BookingEventDto(
    Guid Id,
    BookingEventKind Kind,
    DateTime OccurredAt,
    string Title,
    BookingEventActorDto Actor,
    BookingRescheduleDto? Reschedule,
    string? CancellationReason
);

public record BookingHistoryDto(Guid BookingId, bool PrecedesJournal, List<BookingEventDto> Events);
