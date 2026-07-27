using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/companies/{id:guid}/mail")]
[Authorize]
public class MailingController(AppDbContext db, SubscriptionResolver subscriptionResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SendMail(Guid id, [FromBody] SendMailDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(id);
        if (!plan.AllowMailing) return StatusCode(402, "Mailing requires a tariff plan that includes it.");

        var clientIds = await db.Bookings
            .Where(b => b.CompanyId == id && b.ClientId != null)
            .Select(b => b.ClientId!)
            .Distinct()
            .ToListAsync();

        var emails = await db.Users
            .Where(u => clientIds.Contains(u.Id) && u.Email != null)
            .Select(u => u.Email!)
            .ToListAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var log = new MailLog
        {
            Id = Guid.NewGuid(),
            CompanyId = id,
            Subject = dto.Subject,
            Message = dto.Message,
            SentById = userId,
            RecipientCount = emails.Count
        };
        db.MailLogs.Add(log);
        await db.SaveChangesAsync();

        return Ok(new { recipientCount = emails.Count, message = "Рассылка поставлена в очередь" });
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(Guid id)
    {
        if (!await CanManageCompany(id)) return Forbid();
        var logs = await db.MailLogs
            .Where(m => m.CompanyId == id)
            .OrderByDescending(m => m.SentAt)
            .ToListAsync();
        return Ok(logs);
    }

    private async Task<bool> CanManageCompany(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        if (User.IsInRole("SuperAdmin")) return true;
        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == companyId && cm.UserId == userId && cm.Role == UserRole.CompanyOwner);
    }
}

public record SendMailDto(string Subject, string Message);
