namespace ServiceBooking.API.Services;

public record TimeRange(TimeOnly Start, TimeOnly End);

/// <summary>
/// Replaces the old bare <c>bool allowWithoutSchedule</c> (ARCHITECTURE_CYCLE6.md §46.2, R11): a
/// forgotten call site can no longer silently fall back to a whole day just because it passed `true`
/// where `false` was meant.
/// </summary>
public enum ScheduleFallback
{
    /// <summary>No schedule row for this date → empty slot list. The public/guest path; unchanged behavior.</summary>
    None,
    /// <summary>No schedule row for this date → a configured default window (e.g. 09:00-21:00), not the whole day.</summary>
    DefaultWindow,
    /// <summary>No schedule row for this date → the entire 00:00-24:00 day. Only by explicit staff request.</summary>
    WholeDay,
}

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
        ScheduleFallback fallback,
        TimeOnly defaultWindowStart = default,
        TimeOnly defaultWindowEnd = default)
    {
        // No schedule row for this date at all (ARCHITECTURE_CYCLE6.md §46.2): None -> nothing until
        // the master sets working hours (public/guest path, unchanged); DefaultWindow -> a configured
        // window, not the whole day; WholeDay -> the entire day, only by explicit staff request.
        if (workStart is null && workEnd is null)
        {
            if (fallback == ScheduleFallback.None) return [];
            if (fallback == ScheduleFallback.DefaultWindow)
            {
                workStart = defaultWindowStart;
                workEnd = defaultWindowEnd;
            }
        }

        // `SPEC_CYCLE6_BOOKING_FIXES.md` §0.1 Q7 (поправка 2026-09-22): WholeDay is explicit staff request for "any time
        // convenient for the master", regardless of whether a schedule row exists for the date. The
        // schedule window must be ignored, not only its absence — otherwise the "show other hours"
        // toggle is a no-op on any date that does have a WorkingHours row.
        var ignoreSchedule = fallback == ScheduleFallback.WholeDay;

        var slots = new List<TimeSlotResult>();
        var duration = TimeSpan.FromMinutes(serviceDurationMinutes);
        var current = ignoreSchedule ? TimeSpan.Zero : workStart?.ToTimeSpan() ?? TimeSpan.Zero;
        var end = ignoreSchedule ? TimeSpan.FromHours(24) : workEnd?.ToTimeSpan() ?? TimeSpan.FromHours(24);

        while (current + duration <= end)
        {
            // TimeOnly can't represent 24:00 — stop before the day rolls over instead of throwing.
            if (current + duration >= TimeSpan.FromDays(1)) break;

            var slotStart = TimeOnly.FromTimeSpan(current);
            var slotEnd = TimeOnly.FromTimeSpan(current + duration);

            // WholeDay also ignores breaks (`SPEC_CYCLE6_BOOKING_FIXES.md` §0.1 Q7): the server accepts staff bookings that
            // overlap a break, so hiding break time from the grid would show staff less than they're
            // actually allowed to book.
            var isBreak = !ignoreSchedule && breaks.Any(b => b.Start < slotEnd && b.End > slotStart);
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
        ScheduleFallback fallback,
        TimeOnly defaultWindowStart = default,
        TimeOnly defaultWindowEnd = default)
        => Calculate(serviceDurationMinutes, workStart, workEnd, breaks, bookings, fallback, defaultWindowStart, defaultWindowEnd)
            .Any(s => s.Start == requestedStart);
}
