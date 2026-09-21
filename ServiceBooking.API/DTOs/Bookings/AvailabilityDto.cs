namespace ServiceBooking.API.DTOs.Bookings;

/// <summary>Exactly three server-known day states (ARCHITECTURE_CYCLE6.md §45.1) — "past" is never sent; the browser decides that from its own local date.</summary>
public enum DayAvailabilityStatus
{
    Available,
    FullyBooked,
    DayOff,
}

public record DayAvailabilityDto(DateOnly Date, DayAvailabilityStatus Status, TimeOnly? LastFreeSlotStart);

public record AvailabilityDto(
    DateOnly From, DateOnly To,
    int TotalDurationMinutes, int StepMinutes,
    int HorizonDays, DateOnly HorizonLastDate,
    List<DayAvailabilityDto> Days);
