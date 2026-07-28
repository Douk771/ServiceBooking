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
    public async Task<List<TimeSlotResult>> GetAvailableSlotsAsync(string masterId, Guid serviceId, DateOnly date, bool allowWithoutSchedule = false)
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service is null) return [];

        var workingHours = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.MasterId == masterId && wh.Date == date && wh.IsWorking);

        if (workingHours is null && !allowWithoutSchedule) return [];

        var existingBookings = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date == date && b.Status != BookingStatus.Cancelled)
            .Select(b => new { b.StartTime, b.EndTime })
            .ToListAsync();

        var slots = new List<TimeSlotResult>();
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var current = workingHours?.StartTime.ToTimeSpan() ?? TimeSpan.Zero;
        var end = workingHours?.EndTime.ToTimeSpan() ?? TimeSpan.FromHours(24);

        while (current + duration <= end)
        {
            // TimeOnly can't represent 24:00 — stop before the day rolls over instead of throwing.
            if (current + duration >= TimeSpan.FromDays(1)) break;

            var slotStart = TimeOnly.FromTimeSpan(current);
            var slotEnd   = TimeOnly.FromTimeSpan(current + duration);

            var isBreak  = workingHours?.Breaks.Any(b => b.StartTime < slotEnd && b.EndTime > slotStart) ?? false;
            var isBooked = existingBookings.Any(b => b.StartTime < slotEnd && b.EndTime > slotStart);

            if (!isBreak && !isBooked)
                slots.Add(new TimeSlotResult(slotStart, slotEnd));

            current += TimeSpan.FromMinutes(30);
        }

        return slots;
    }
}

public record TimeSlotResult(TimeOnly Start, TimeOnly End);
