using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/schedule-template")]
public class ScheduleTemplateController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get([FromQuery] string masterId, [FromQuery] Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await CanManage(masterId, companyId, userId)) return Forbid();

        var templates = await db.WeeklyScheduleTemplates
            .Where(t => t.MasterId == masterId && t.CompanyId == companyId)
            .OrderBy(t => t.DayOfWeek)
            .Select(t => new
            {
                t.Id,
                t.DayOfWeek,
                t.IsWorking,
                t.StartTime,
                t.EndTime
            })
            .ToListAsync();

        return Ok(templates);
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> Put([FromBody] PutTemplateRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await CanManage(request.MasterId, request.CompanyId, userId)) return Forbid();

        // Serialize concurrent Put calls for the same master+company so the delete-then-insert below
        // is atomic — otherwise two simultaneous requests could interleave and leave a mix of old and
        // new rows (CURRENT_STATE §6, audit B3).
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"schedule-template:{request.MasterId}:{request.CompanyId}");

        var old = db.WeeklyScheduleTemplates
            .Where(t => t.MasterId == request.MasterId && t.CompanyId == request.CompanyId);
        db.WeeklyScheduleTemplates.RemoveRange(old);

        var newTemplates = request.Days.Select(d => new WeeklyScheduleTemplate
        {
            Id = Guid.NewGuid(),
            MasterId = request.MasterId,
            CompanyId = request.CompanyId,
            DayOfWeek = d.DayOfWeek,
            IsWorking = d.IsWorking,
            StartTime = d.StartTime,
            EndTime = d.EndTime
        });

        await db.WeeklyScheduleTemplates.AddRangeAsync(newTemplates);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok();
    }

    [HttpPost("apply")]
    [Authorize]
    public async Task<IActionResult> Apply(
        [FromQuery] string masterId,
        [FromQuery] Guid companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await CanManage(masterId, companyId, userId)) return Forbid();

        if (to < from) return BadRequest("Invalid date range: 'to' must not be earlier than 'from'.");
        // 366, not 365 — a full leap-year range must be applyable in a single call, which is the
        // normal case for "apply my template for the whole year" (audit B4).
        if (to.DayNumber - from.DayNumber > 366)
            return BadRequest("Date range is too large: at most 366 days can be applied at once.");

        // Serialize concurrent Apply calls for the same master+company so the find-or-create loop below
        // is atomic — otherwise two simultaneous requests could both miss the same existing row and both
        // insert, producing a duplicate that the unique index would then reject.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"working-hours:{masterId}:{companyId}");

        var templates = await db.WeeklyScheduleTemplates
            .Where(t => t.MasterId == masterId && t.CompanyId == companyId)
            .ToListAsync();

        if (!templates.Any())
            return BadRequest("No template found for this master/company.");

        var existing = await db.WorkingHours
            .Where(wh => wh.MasterId == masterId && wh.CompanyId == companyId && wh.Date >= from && wh.Date <= to)
            .ToListAsync();

        var existingByDate = existing.ToDictionary(wh => wh.Date);

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            // ISO DayOfWeek: Monday=1 ... Sunday=7
            var iso = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            var tmpl = templates.FirstOrDefault(t => t.DayOfWeek == iso);
            if (tmpl == null) continue;

            if (existingByDate.TryGetValue(date, out var wh))
            {
                wh.IsWorking = tmpl.IsWorking;
                wh.StartTime = tmpl.StartTime;
                wh.EndTime = tmpl.EndTime;
            }
            else
            {
                db.WorkingHours.Add(new WorkingHours
                {
                    Id = Guid.NewGuid(),
                    MasterId = masterId,
                    CompanyId = companyId,
                    Date = date,
                    IsWorking = tmpl.IsWorking,
                    StartTime = tmpl.StartTime,
                    EndTime = tmpl.EndTime
                });
            }
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { message = "Template applied successfully." });
    }

    private async Task<bool> CanManage(string masterId, Guid companyId, string requesterId)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        // Being the master is not enough on its own: without the membership check any authenticated user
        // could pass their own id with an arbitrary companyId and write themselves a schedule template
        // inside a company they have nothing to do with (audit A5). It also revokes access as soon as a
        // master is removed from the company.
        if (requesterId == masterId) return await CompanyMembership.IsStaffAsync(db, companyId, requesterId);
        return await CompanyMembership.IsOwnerAsync(db, companyId, requesterId);
    }
}

public record DayTemplate(int DayOfWeek, bool IsWorking, TimeOnly StartTime, TimeOnly EndTime);
public record PutTemplateRequest(string MasterId, Guid CompanyId, List<DayTemplate> Days);
