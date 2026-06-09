using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

public class SlotService(AppDbContext db)
{
    public async Task<List<TimeSlotResult>> GetAvailableSlotsAsync(string masterId, Guid serviceId, DateOnly date)
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service is null) return [];

        var dayOfWeek = date.DayOfWeek;
        var workingHours = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.MasterId == masterId && wh.DayOfWeek == dayOfWeek && wh.IsWorking);

        if (workingHours is null) return [];

        var existingBookings = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date == date &&
                        b.Status != BookingStatus.Cancelled)
            .Select(b => new { b.StartTime, b.EndTime })
            .ToListAsync();

        var slots = new List<TimeSlotResult>();
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var current = workingHours.StartTime.ToTimeSpan();
        var end = workingHours.EndTime.ToTimeSpan();

        while (current + duration <= end)
        {
            var slotStart = TimeOnly.FromTimeSpan(current);
            var slotEnd = TimeOnly.FromTimeSpan(current + duration);

            var isBreak = workingHours.Breaks.Any(b => b.StartTime < slotEnd && b.EndTime > slotStart);
            var isBooked = existingBookings.Any(b => b.StartTime < slotEnd && b.EndTime > slotStart);

            if (!isBreak && !isBooked)
                slots.Add(new TimeSlotResult(slotStart, slotEnd));

            current += TimeSpan.FromMinutes(30); // 30-minute step
        }

        return slots;
    }
}

public record TimeSlotResult(TimeOnly Start, TimeOnly End);
