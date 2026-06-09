using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.WorkingHours;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkingHoursController(AppDbContext db) : ControllerBase
{
    // GET /api/workinghours?masterId=&companyId=&from=2024-01-01&to=2024-01-31
    [HttpGet]
    public async Task<ActionResult<List<WorkingHoursDto>>> Get(
        [FromQuery] string masterId,
        [FromQuery] Guid companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        var hours = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .Where(wh => wh.MasterId == masterId && wh.CompanyId == companyId
                      && wh.Date >= from && wh.Date <= to)
            .OrderBy(wh => wh.Date)
            .ToListAsync();

        return Ok(hours.Select(ToDto).ToList());
    }

    [HttpPut]
    public async Task<ActionResult<WorkingHoursDto>> Upsert(UpsertWorkingHoursDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await CanManage(dto.MasterId, dto.CompanyId, userId)) return Forbid();

        var existing = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh =>
                wh.MasterId == dto.MasterId &&
                wh.CompanyId == dto.CompanyId &&
                wh.Date == dto.Date);

        if (existing is null)
        {
            existing = new WorkingHours
            {
                Id = Guid.NewGuid(),
                MasterId = dto.MasterId,
                CompanyId = dto.CompanyId,
                Date = dto.Date,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                IsWorking = dto.IsWorking
            };
            db.WorkingHours.Add(existing);
        }
        else
        {
            existing.IsWorking = dto.IsWorking;
            existing.StartTime = dto.StartTime;
            existing.EndTime = dto.EndTime;
            db.ScheduleBreaks.RemoveRange(existing.Breaks);
            existing.Breaks.Clear();
        }

        foreach (var b in dto.Breaks)
            existing.Breaks.Add(new ScheduleBreak
            {
                Id = Guid.NewGuid(),
                WorkingHoursId = existing.Id,
                StartTime = b.StartTime,
                EndTime = b.EndTime
            });

        await db.SaveChangesAsync();
        return Ok(ToDto(existing));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var entry = await db.WorkingHours.FindAsync(id);
        if (entry is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await CanManage(entry.MasterId, entry.CompanyId, userId)) return Forbid();

        db.WorkingHours.Remove(entry);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> CanManage(string masterId, Guid companyId, string requesterId)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        if (requesterId == masterId) return true;
        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == companyId &&
            cm.UserId == requesterId &&
            cm.Role == UserRole.CompanyOwner);
    }

    private static WorkingHoursDto ToDto(WorkingHours wh) =>
        new(wh.Id, wh.MasterId, wh.CompanyId, wh.Date, wh.StartTime, wh.EndTime, wh.IsWorking,
            wh.Breaks.Select(b => new BreakDto(b.Id, b.StartTime, b.EndTime)).ToList());
}
