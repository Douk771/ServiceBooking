using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "CompanyOwner,SuperAdmin")]
public class ReportsController(AppDbContext db, SubscriptionResolver subscriptionResolver) : ControllerBase
{
    [HttpGet("masters")]
    public async Task<ActionResult<List<MasterReportDto>>> GetMastersReport(
        [FromQuery] Guid companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Verify caller owns this company
        if (!User.IsInRole("SuperAdmin") && !await CompanyMembership.IsOwnerAsync(db, companyId, userId))
            return Forbid();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(companyId);
        if (!plan.AllowAnalytics) return StatusCode(402, "Analytics requires a tariff plan that includes it.");

        // Cycle 22 (§375 F4): a read-only projection of just the fields the report uses, instead of
        // tracked Booking entities with the whole Master row included.
        var bookings = await db.Bookings
            .AsNoTracking()
            .Where(b => b.CompanyId == companyId &&
                        b.Status == BookingStatus.Completed &&
                        b.Date >= from && b.Date <= to)
            .Select(b => new
            {
                b.MasterId, MasterFirstName = b.Master.FirstName, MasterLastName = b.Master.LastName,
                b.Date, b.StartTime, b.Price, b.CommissionPercent,
            })
            .ToListAsync(ct);

        // Commission is per-BOOKING (snapshotted at creation time, same as Price), not read from the
        // current CompanyMembers row: that row can change rate or disappear entirely (a master leaving
        // the company) without retroactively altering a historical, already-closed report. See the
        // comment on Booking.CommissionPercent.
        var result = bookings
            .GroupBy(b => b.MasterId)
            .Select(g =>
            {
                var master = g.First();
                // All bookings in the group share the same master, but not necessarily the same
                // commission rate if it changed mid-period — report the rate of the latest booking as
                // representative, matching how a single "current rate" figure is normally understood.
                var commissionPercent = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                    .First().CommissionPercent;
                var total = g.Sum(b => b.Price);
                var commission = g.Sum(b => Math.Round(b.Price * b.CommissionPercent / 100, 2));
                return new MasterReportDto(
                    g.Key, $"{master.MasterFirstName} {master.MasterLastName}",
                    commissionPercent, g.Count(), total, commission, total - commission);
            })
            .OrderByDescending(r => r.TotalAmount)
            .ToList();

        return Ok(result);
    }
}

public record MasterReportDto(
    string MasterId, string MasterName, decimal CommissionPercent,
    int BookingsCount, decimal TotalAmount, decimal MasterEarnings, decimal CompanyEarnings);
