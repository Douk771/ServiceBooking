using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Companies;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the body of <c>GET /api/companies/{id}/stats</c>, moved
/// verbatim out of <c>CompaniesController.GetStats</c> — the same SQL aggregates in the same order, the
/// same anonymous response shape. The controller keeps the access check and the from/to validation.
/// </summary>
public sealed class CompanyStatsService(AppDbContext db)
{
    /// <summary>The report body the action returns with <c>Ok(...)</c>; <paramref name="from"/> and
    /// <paramref name="to"/> are the action's own, already null- and order-checked, values.</summary>
    public async Task<object> GetStatsAsync(Guid id, DateTime from, DateTime to, CancellationToken ct)
    {
        // The date of truth is the VISIT date (Booking.Date), not the date the booking row was created
        // (decision Q11) — a booking made on June 30th for a July 5th visit must land in the July report,
        // matching GET /api/reports/masters (ReportsController.cs), which already filters by Date. Before
        // this fix the two reports could disagree about which period a booking belonged to.
        var fromDate = DateOnly.FromDateTime(from);
        var toDate = DateOnly.FromDateTime(to);

        // Cycle 22 (§375 F3, closes the second half of §9.19): every figure is an aggregate computed
        // in SQL over the period's bookings — the period is no longer loaded into memory (and the
        // unused Service include is gone). Money stays decimal end to end (numeric SUM in Postgres),
        // so the totals and their scale are what the in-memory decimal sums produced (CY22-09/10).
        var period = db.Bookings.AsNoTracking()
            .Where(b => b.CompanyId == id && b.Date >= fromDate && b.Date <= toDate);
        var completed = period.Where(b => b.Status == BookingStatus.Completed);

        var countsByStatus = await period
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(BookingStatus status) => countsByStatus.Where(c => c.Status == status).Sum(c => c.Count);

        // New clients: first VISIT in this company falls in [fromDate, toDate] — same date semantics as
        // the revenue filter above (ARCHITECTURE.md §14.3), so a single response never mixes "first
        // created" and "first visited" as two different meanings of "new".
        var newClientsCount = await db.Bookings
            .Where(b => b.CompanyId == id && b.ClientId != null)
            .GroupBy(b => b.ClientId!)
            .Select(g => g.Min(b => b.Date))
            .CountAsync(firstDate => firstDate >= fromDate && firstDate <= toDate, ct);

        var byMaster = await completed
            .GroupBy(b => b.MasterId)
            .Select(g => new { MasterId = g.Key, BookingsCount = g.Count(), Revenue = g.Sum(b => b.Price) })
            .ToListAsync(ct);
        var masterIds = byMaster.Select(m => m.MasterId).ToList();
        var masterNames = await db.Users.AsNoTracking()
            .Where(u => masterIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var masterStats = byMaster
            .Select(g => new
            {
                masterId = g.MasterId,
                masterName = masterNames.TryGetValue(g.MasterId, out var name) ? name : g.MasterId,
                bookingsCount = g.BookingsCount,
                revenue = g.Revenue
            }).ToList();

        // US-67 (ARCHITECTURE_CYCLE6.md §44.2 p.4): the "top services" breakdown counts individual
        // services from BookingServices, not visits — a 3-service visit contributes 3 counts here,
        // one per line item, while totalRevenue below (computed from Booking.Price) still counts the
        // visit exactly once. Pre-cycle bookings have exactly one BookingServices row each (backfilled),
        // so this is unchanged for them. serviceName: the name snapshot of the service's rows (the old
        // code took the first row's; rows of one service only differ after a rename — MAX picks one of
        // them deterministically).
        var popularServices = (await db.BookingServices.AsNoTracking()
                .Where(bs => period.Any(b => b.Id == bs.BookingId))
                .GroupBy(bs => bs.ServiceId)
                .Select(g => new { serviceId = g.Key, serviceName = g.Max(bs => bs.NameSnapshot)!, count = g.Count() })
                .ToListAsync(ct))
            .OrderByDescending(s => s.count)
            .ToList();

        var dailyRevenue = await completed
            .GroupBy(b => b.Date)
            .Select(g => new { date = g.Key, revenue = g.Sum(b => b.Price) })
            .OrderBy(d => d.date)
            .ToListAsync(ct);

        // Summed in C# from the per-day sums (exact decimal arithmetic, the same result as summing the
        // bookings): an empty period yields decimal 0 — serialized "0", as before — rather than SQL's
        // COALESCE(SUM(...), 0.0).
        var totalRevenue = dailyRevenue.Sum(d => d.revenue);

        return new
        {
            totalRevenue,
            bookingsCount = countsByStatus.Sum(c => c.Count),
            completedCount = CountOf(BookingStatus.Completed),
            cancelledCount = CountOf(BookingStatus.Cancelled),
            newClientsCount,
            masterStats,
            popularServices,
            dailyRevenue
        };
    }
}
