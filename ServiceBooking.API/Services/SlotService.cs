using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

public class SlotService(AppDbContext db)
{
    // `allowWithoutSchedule` lets a staff member creating a manual booking on a client's behalf pick
    // any free slot across the whole day even when the master hasn't set working hours for this date
    // yet. Guest/self-service booking never sets this — it stays gated to explicitly configured hours,
    // which is what protects a master from being booked at a time they never agreed to.
    public async Task<List<TimeSlotResult>> GetAvailableSlotsAsync(Guid companyId, string masterId, Guid serviceId, DateOnly date, bool allowWithoutSchedule = false)
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service is null) return [];

        var workingHours = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.MasterId == masterId && wh.CompanyId == companyId && wh.Date == date && wh.IsWorking);

        // Guard duplicates the rule intentionally to skip the bookings query; the authoritative rule
        // lives in SlotCalculator.
        if (workingHours is null && !allowWithoutSchedule) return [];

        // Occupancy is deliberately NOT scoped by company: a master who works for two businesses is
        // still one person, so a booking made in company A must block the same time in company B.
        // Working hours ARE scoped by company (a master can keep different schedules) — the asymmetry
        // is intentional.
        var existingBookings = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date == date && b.Status != BookingStatus.Cancelled)
            .Select(b => new TimeRange(b.StartTime, b.EndTime))
            .ToListAsync();

        var breaks = workingHours?.Breaks.Select(b => new TimeRange(b.StartTime, b.EndTime)).ToList() ?? [];

        return SlotCalculator.Calculate(
            service.DurationMinutes,
            workingHours?.StartTime, workingHours?.EndTime,
            breaks, existingBookings, allowWithoutSchedule);
    }
}

public record TimeSlotResult(TimeOnly Start, TimeOnly End);
