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
        [FromQuery] DateOnly to)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Verify caller owns this company
        if (!User.IsInRole("SuperAdmin") && !await CompanyMembership.IsOwnerAsync(db, companyId, userId))
            return Forbid();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(companyId);
        if (!plan.AllowAnalytics) return StatusCode(402, "Analytics requires a tariff plan that includes it.");

        var bookings = await db.Bookings
            .Include(b => b.Master)
            .Where(b => b.CompanyId == companyId &&
                        b.Status == BookingStatus.Completed &&
                        b.Date >= from && b.Date <= to)
            .ToListAsync();

        // Commission is per-BOOKING (snapshotted at creation time, same as Price), not read from the
        // current CompanyMembers row: that row can change rate or disappear entirely (a master leaving
        // the company) without retroactively altering a historical, already-closed report. See the
        // comment on Booking.CommissionPercent.
        var result = bookings
            .GroupBy(b => b.Master)
            .Select(g =>
            {
                var master = g.Key;
                // All bookings in the group share the same master, but not necessarily the same
                // commission rate if it changed mid-period — report the rate of the latest booking as
                // representative, matching how a single "current rate" figure is normally understood.
                var commissionPercent = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                    .First().CommissionPercent;
                var total = g.Sum(b => b.Price);
                var commission = g.Sum(b => Math.Round(b.Price * b.CommissionPercent / 100, 2));
                return new MasterReportDto(
                    master.Id, $"{master.FirstName} {master.LastName}",
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
