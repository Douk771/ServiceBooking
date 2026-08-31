namespace ServiceBooking.API.Services;

public record TimeRange(TimeOnly Start, TimeOnly End);

/// <summary>
/// Pure slot-grid logic shared by slot listing and slot validation (US-03, ARCHITECTURE.md §2.2).
/// No DB access, no EF types — the single source of truth for "which start times a master can be
/// booked at on this date", so the server-side check in Create/Reschedule can never drift from what
/// GET /api/bookings/slots advertised.
/// </summary>
public static class SlotCalculator
{
    public const int StepMinutes = 30;

    /// <summary>Pure slot grid: no DB access, no EF types. The single source of truth for
    /// "which start times this master can be booked at on this date".</summary>
    public static List<TimeSlotResult> Calculate(
        int serviceDurationMinutes,
        TimeOnly? workStart, TimeOnly? workEnd,
        IReadOnlyList<TimeRange> breaks,
        IReadOnlyList<TimeRange> bookings,
        bool allowWithoutSchedule)
    {
        // No schedule row for this date at all: staff manual booking may still pick any free slot
        // across the whole day; everyone else gets nothing until the master sets working hours.
        if (workStart is null && workEnd is null && !allowWithoutSchedule) return [];

        var slots = new List<TimeSlotResult>();
        var duration = TimeSpan.FromMinutes(serviceDurationMinutes);
        var current = workStart?.ToTimeSpan() ?? TimeSpan.Zero;
        var end = workEnd?.ToTimeSpan() ?? TimeSpan.FromHours(24);

        while (current + duration <= end)
        {
            // TimeOnly can't represent 24:00 — stop before the day rolls over instead of throwing.
            if (current + duration >= TimeSpan.FromDays(1)) break;

            var slotStart = TimeOnly.FromTimeSpan(current);
            var slotEnd = TimeOnly.FromTimeSpan(current + duration);

            var isBreak = breaks.Any(b => b.Start < slotEnd && b.End > slotStart);
            var isBooked = bookings.Any(b => b.Start < slotEnd && b.End > slotStart);

            if (!isBreak && !isBooked)
                slots.Add(new TimeSlotResult(slotStart, slotEnd));

            current += TimeSpan.FromMinutes(StepMinutes);
        }

        return slots;
    }

    /// <summary>The booking-time check. Deliberately implemented ON TOP of Calculate rather than as a
    /// second predicate: a separate implementation of "inside working hours, not in a break, on the
    /// 30-minute grid" is exactly how GetStats and ReportsController drifted apart (audit B5).</summary>
    public static bool IsSlotAllowed(
        TimeOnly requestedStart,
        int serviceDurationMinutes,
        TimeOnly? workStart, TimeOnly? workEnd,
        IReadOnlyList<TimeRange> breaks,
        IReadOnlyList<TimeRange> bookings,
        bool allowWithoutSchedule)
        => Calculate(serviceDurationMinutes, workStart, workEnd, breaks, bookings, allowWithoutSchedule)
            .Any(s => s.Start == requestedStart);
}
