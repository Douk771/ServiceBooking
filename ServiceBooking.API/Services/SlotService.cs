using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

public class SlotService(AppDbContext db, IConfiguration configuration)
{
    // Default staff fallback window when there's no schedule row for the date and extendedHours
    // wasn't requested (ARCHITECTURE_CYCLE6.md §46.2), read from Booking:DefaultWorkWindow. Parsing
    // lives in DeploymentSafetyChecks (pure, unit-testable); an unparsable/inverted value throws at
    // first use rather than silently falling back to a whole day.
    private (TimeOnly Start, TimeOnly End)? _defaultWindow;

    /// <summary>Public so <see cref="AvailabilityService"/> and BookingsController's callers use the
    /// exact same parsed config value instead of re-parsing it themselves.</summary>
    public (TimeOnly Start, TimeOnly End) GetDefaultWindow() =>
        _defaultWindow ??= DeploymentSafetyChecks.ParseDefaultWorkWindow(configuration);

    // `fallback` lets a staff member creating a manual booking on a client's behalf pick a free slot
    // even when the master hasn't set working hours for this date yet — DefaultWindow (09:00-21:00 by
    // default) unless they explicitly ask for the whole day. Guest/self-service booking always passes
    // None — it stays gated to explicitly configured hours, which is what protects a master from being
    // booked at a time they never agreed to.
    public async Task<List<TimeSlotResult>> GetAvailableSlotsAsync(
        Guid companyId, string masterId, Guid serviceId, DateOnly date, ScheduleFallback fallback = ScheduleFallback.None)
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service is null) return [];

        var workingHours = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.MasterId == masterId && wh.CompanyId == companyId && wh.Date == date && wh.IsWorking);

        // Guard duplicates the rule intentionally to skip the bookings query; the authoritative rule
        // lives in SlotCalculator.
        if (workingHours is null && fallback == ScheduleFallback.None) return [];

        // Occupancy is deliberately NOT scoped by company: a master who works for two businesses is
        // still one person, so a booking made in company A must block the same time in company B.
        // Working hours ARE scoped by company (a master can keep different schedules) — the asymmetry
        // is intentional.
        var existingBookings = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date == date && b.Status != BookingStatus.Cancelled)
            .Select(b => new TimeRange(b.StartTime, b.EndTime))
            .ToListAsync();

        var breaks = workingHours?.Breaks.Select(b => new TimeRange(b.StartTime, b.EndTime)).ToList() ?? [];

        var (defaultStart, defaultEnd) = GetDefaultWindow();
        return SlotCalculator.Calculate(
            service.DurationMinutes,
            workingHours?.StartTime, workingHours?.EndTime,
            breaks, existingBookings, fallback, defaultStart, defaultEnd);
    }
}

public record TimeSlotResult(TimeOnly Start, TimeOnly End);
