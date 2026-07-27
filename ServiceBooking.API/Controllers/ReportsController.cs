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
        if (!User.IsInRole("SuperAdmin"))
        {
            var isMember = await db.CompanyMembers.AnyAsync(cm =>
                cm.CompanyId == companyId && cm.UserId == userId && cm.Role == UserRole.CompanyOwner);
            if (!isMember) return Forbid();
        }

        var plan = await subscriptionResolver.GetEffectivePlanAsync(companyId);
        if (!plan.AllowAnalytics) return StatusCode(402, "Analytics requires a tariff plan that includes it.");

        var bookings = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Where(b => b.CompanyId == companyId &&
                        b.Status == BookingStatus.Completed &&
                        b.Date >= from && b.Date <= to)
            .ToListAsync();

        var result = bookings
            .GroupBy(b => b.Master)
            .Select(g =>
            {
                var master = g.Key;
                var total = g.Sum(b => b.Price);
                var commission = Math.Round(total * master.CommissionPercent / 100, 2);
                return new MasterReportDto(
                    master.Id, $"{master.FirstName} {master.LastName}",
                    master.CommissionPercent, g.Count(), total, commission, total - commission);
            })
            .OrderByDescending(r => r.TotalAmount)
            .ToList();

        return Ok(result);
    }
}

public record MasterReportDto(
    string MasterId, string MasterName, decimal CommissionPercent,
    int BookingsCount, decimal TotalAmount, decimal MasterEarnings, decimal CompanyEarnings);
