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
    [HttpGet]
    public async Task<ActionResult<List<WorkingHoursDto>>> Get(
        [FromQuery] string masterId,
        [FromQuery] Guid companyId)
    {
        var hours = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .Where(wh => wh.MasterId == masterId && wh.CompanyId == companyId)
            .OrderBy(wh => wh.DayOfWeek)
            .ToListAsync();

        return Ok(hours.Select(ToDto).ToList());
    }

    [HttpPut]
    public async Task<ActionResult<WorkingHoursDto>> Upsert(UpsertWorkingHoursDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (!await CanManage(dto.MasterId, dto.CompanyId, userId))
            return Forbid();

        var existing = await db.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh =>
                wh.MasterId == dto.MasterId &&
                wh.CompanyId == dto.CompanyId &&
                wh.DayOfWeek == dto.DayOfWeek);

        if (existing is null)
        {
            existing = new WorkingHours
            {
                Id = Guid.NewGuid(),
                MasterId = dto.MasterId,
                CompanyId = dto.CompanyId,
                DayOfWeek = dto.DayOfWeek,
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
            existing.Breaks.Add(new ScheduleBreak { Id = Guid.NewGuid(), WorkingHoursId = existing.Id, StartTime = b.StartTime, EndTime = b.EndTime });

        await db.SaveChangesAsync();
        return Ok(ToDto(existing));
    }

    private async Task<bool> CanManage(string masterId, Guid companyId, string requesterId)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        // Master can edit own schedule
        if (requesterId == masterId) return true;
        // Owner can edit any master's schedule in their company
        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == companyId &&
            cm.UserId == requesterId &&
            cm.Role == UserRole.CompanyOwner);
    }

    private static WorkingHoursDto ToDto(WorkingHours wh) =>
        new(wh.Id, wh.MasterId, wh.CompanyId, wh.DayOfWeek, wh.StartTime, wh.EndTime, wh.IsWorking,
            wh.Breaks.Select(b => new BreakDto(b.Id, b.StartTime, b.EndTime)).ToList());
}
