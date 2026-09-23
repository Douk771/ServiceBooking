using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

/// <summary>
/// US-65 (ARCHITECTURE_CYCLE6.md §45.3): a thin wrapper over three range queries, exactly like
/// <see cref="SlotService"/> is over one date's worth — the per-day status itself is computed by the
/// SAME <see cref="SlotCalculator.Calculate"/> that answers <c>GET /api/bookings/slots</c>, so the
/// calendar and the slot grid can never drift apart.
/// </summary>
public class AvailabilityService(AppDbContext db)
{
    public async Task<List<DayAvailabilityDto>> GetAvailabilityAsync(
        Guid companyId, string masterId, int totalDurationMinutes, DateOnly from, DateOnly to,
        ScheduleFallback fallback, TimeOnly defaultWindowStart, TimeOnly defaultWindowEnd,
        // ARCHITECTURE_CYCLE10.md §103.2: scheduleState is filled if and only if the caller was
        // recognized as staff — passed in explicitly rather than derived from `fallback`, because
        // `fallback` alone can't tell "staff caller, WorkingHours row happens to say IsWorking=true"
        // apart from "non-staff caller, same row" — both compute the same slots.
        bool staffMode = false)
    {
        var workingHoursByDate = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .Where(wh => wh.MasterId == masterId && wh.CompanyId == companyId && wh.Date >= from && wh.Date <= to)
            .ToListAsync();
        var workingHoursMap = workingHoursByDate.ToDictionary(wh => wh.Date);

        // Not scoped by company — occupancy is cross-company (Q9, same as SlotService).
        var bookingsByDate = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date >= from && b.Date <= to && b.Status != BookingStatus.Cancelled)
            .Select(b => new { b.Date, Range = new TimeRange(b.StartTime, b.EndTime) })
            .ToListAsync();
        var bookingsMap = bookingsByDate.GroupBy(b => b.Date).ToDictionary(g => g.Key, g => g.Select(x => x.Range).ToList());

        var days = new List<DayAvailabilityDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            workingHoursMap.TryGetValue(date, out var workingHours);
            var isWorking = workingHours is { IsWorking: true };
            var breaks = isWorking ? workingHours!.Breaks.Select(b => new TimeRange(b.StartTime, b.EndTime)).ToList() : [];
            var bookings = bookingsMap.GetValueOrDefault(date, []);

            // §103.2: DayScheduleState is informational only for staff — never affects `status` above.
            DayScheduleState? scheduleState = !staffMode ? null
                : workingHours is null ? DayScheduleState.NoSchedule
                : workingHours.IsWorking ? DayScheduleState.Working
                : DayScheduleState.DayOff;

            if (!isWorking && fallback == ScheduleFallback.None)
            {
                days.Add(new DayAvailabilityDto(date, DayAvailabilityStatus.DayOff, null, scheduleState));
                continue;
            }

            var slots = SlotCalculator.Calculate(
                totalDurationMinutes,
                isWorking ? workingHours!.StartTime : null, isWorking ? workingHours!.EndTime : null,
                breaks, bookings, fallback, defaultWindowStart, defaultWindowEnd);

            if (slots.Count == 0)
            {
                days.Add(new DayAvailabilityDto(date, DayAvailabilityStatus.FullyBooked, null, scheduleState));
                continue;
            }

            days.Add(new DayAvailabilityDto(date, DayAvailabilityStatus.Available, slots[^1].Start, scheduleState));
        }

        return days;
    }
}
