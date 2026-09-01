using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.WorkingHours;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await CanManage(masterId, companyId, userId)) return Forbid();

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

        // Serialize concurrent Upsert calls for the same master+company+date so the find-or-create
        // below is atomic — otherwise two simultaneous requests could both miss the existing row and
        // both insert, leaving two WorkingHours rows for the same day (CURRENT_STATE §6, audit B3).
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Deliberately locked on the (master, company) pair rather than the individual date: the
        // schedule template's Apply writes a whole range under that same coarser key, and a per-date
        // key here would hash to a different lock — the two would not exclude each other, both would
        // see "no row for this date", and the second INSERT would hit the unique index and surface as
        // a 500. Coarser than strictly needed for a single day, correct against Apply.
        await AdvisoryLock.AcquireAsync(db, $"working-hours:{dto.MasterId}:{dto.CompanyId}");

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
        await transaction.CommitAsync();
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
        // Being the master is not enough on its own: without the membership check any authenticated user
        // could pass their own id with an arbitrary companyId and write themselves working hours inside a
        // company they have nothing to do with (audit A5). It also revokes access as soon as a master is
        // removed from the company.
        if (requesterId == masterId) return await CompanyMembership.IsStaffAsync(db, companyId, requesterId);
        return await CompanyMembership.IsOwnerAsync(db, companyId, requesterId);
    }

    private static WorkingHoursDto ToDto(WorkingHours wh) =>
        new(wh.Id, wh.MasterId, wh.CompanyId, wh.Date, wh.StartTime, wh.EndTime, wh.IsWorking,
            wh.Breaks.Select(b => new BreakDto(b.Id, b.StartTime, b.EndTime)).ToList());
}
