namespace ServiceBooking.API.DTOs.Bookings;

/// <summary>Exactly three server-known day states (ARCHITECTURE_CYCLE6.md §45.1) — "past" is never sent; the browser decides that from its own local date.</summary>
public enum DayAvailabilityStatus
{
    Available,
    FullyBooked,
    DayOff,
}

/// <summary>
/// ARCHITECTURE_CYCLE10.md §103.2: the state of the master's SCHEDULE on this date, independent of
/// whether it can be booked into. Only ever filled when AvailabilityDto.StaffMode is true.
/// </summary>
public enum DayScheduleState
{
    Working,
    DayOff,
    NoSchedule,
}

/// <summary>
/// ARCHITECTURE_CYCLE10.md §103.2: ScheduleState is additive and, unlike Status, is filled ONLY when
/// the caller was recognized as staff (AvailabilityDto.StaffMode == true) — for everyone else it is
/// always null, deliberately, so an anonymous visitor cannot distinguish "day off" from "no schedule
/// filled in yet" for a stranger's master.
/// </summary>
public record DayAvailabilityDto(DateOnly Date, DayAvailabilityStatus Status, TimeOnly? LastFreeSlotStart, DayScheduleState? ScheduleState = null);

/// <summary>
/// ARCHITECTURE_CYCLE10.md §103.3: StaffMode is the ONLY signal the frontend is allowed to use to
/// decide whether to render staff-mode controls — never the caller's role claim, never the requested
/// `manual` flag, both of which are merely a request the server may decline.
/// </summary>
public record AvailabilityDto(
    DateOnly From, DateOnly To,
    int TotalDurationMinutes, int StepMinutes,
    int HorizonDays, DateOnly HorizonLastDate,
    List<DayAvailabilityDto> Days,
    bool StaffMode = false);
