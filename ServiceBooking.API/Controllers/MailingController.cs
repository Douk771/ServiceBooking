using System.ComponentModel.DataAnnotations;
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

    // TD-11 (ARCHITECTURE_CYCLE16.md §254): delegates to the single shared implementation.
    // superAdminBypass stays true — this controller's existing behavior (SuperAdmin could always send
    // mail on behalf of a company).
    private Task<bool> CanManageCompany(Guid companyId) =>
        CompanyAccess.CanManageCompanyAsync(db, User, companyId);
}

// TD-09 (ARCHITECTURE_CYCLE16.md §252, API_CONTRACT_CYCLE16.md §279): validation attributes so a blank
// or unbounded Subject/Message can no longer reach storage. Model-state failures are already converted
// to a bare 400 text/plain string by InvalidModelStateResponseFactory (Program.cs) — this does not
// introduce ProblemDetails and does not change the success response shape.
public record SendMailDto(
    [property: Required(ErrorMessage = "Тема письма обязательна")]
    [property: StringLength(200, ErrorMessage = "Тема письма не может быть длиннее 200 символов")]
    string Subject,
    [property: Required(ErrorMessage = "Текст письма обязателен")]
    [property: StringLength(10000, ErrorMessage = "Текст письма не может быть длиннее 10000 символов")]
    string Message);
