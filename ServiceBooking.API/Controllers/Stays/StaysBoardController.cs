using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// API_CONTRACT_CYCLE37.md §37.29, §37.31 — the board, the blocks of dates and the cleaning schedule. Rights: board — ViewBookings; blocks — ManageBlocks;
/// schedule — ViewSchedule (the only screen of a housekeeper).
/// </summary>
[ApiController]
[Route("api/stays/companies/{companyId:guid}")]
[Authorize]
public class StaysBoardController(
    AppDbContext db, StaysAccessResolver access, StaysBoardService board, HouseBlockWriter blocks, StayActorResolver actors, StaysScheduleService schedule,
    IStaysClock clock) : ControllerBase
{
    [HttpGet("board")]
    [EnableRateLimiting("stays-board")]
    public async Task<ActionResult<StaysBoardDto>> Board(
        Guid companyId, [FromQuery] string? from, [FromQuery] int? days, [FromQuery] long? sinceRevision, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        DateOnly? fromDate = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!StaysCatalogService.TryDate(from, out var parsed)) return BadRequest("Неверный формат даты");
            fromDate = parsed;
        }
        return Ok(await board.BuildAsync(r.Company!, fromDate, days, sinceRevision, ct));
    }

    [HttpPost("blocks")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseBlockDto>> CreateBlock(Guid companyId, HouseBlockInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBlocks, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var company = r.Company!;
        var comment = StaysSettingsRules.Trim(input.Comment);
        var today = StayTime.LocalDate(company.TimeZoneId, clock.UtcNow);
        var error = HouseBlockWriter.Validate(input.StartDate, input.EndDate, comment, today, previousStart: null);
        if (error is not null) return BadRequest(error);
        if (!Enum.IsDefined(input.Kind)) return BadRequest("Неизвестный тип блокировки");
        if (input.HouseId is not { } houseId || !await db.Houses.AsNoTracking().AnyAsync(h => h.Id == houseId && h.CompanyId == companyId, ct)) return NotFound();

        var actor = await actors.ResolveStaffAsync(User, ct);
        var result = await blocks.CreateAsync(company, houseId, input.StartDate!.Value, input.EndDate!.Value, input.Kind, comment, actor, ct);
        if (result.Error is not null) return result.Error;
        return StatusCode(StatusCodes.Status201Created, StaysBoardService.ToDto(result.Block!, actor.NameSnapshot ?? string.Empty));
    }

    [HttpPut("blocks/{blockId:guid}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseBlockDto>> UpdateBlock(Guid companyId, Guid blockId, HouseBlockInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBlocks, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var company = r.Company!;
        var block = await db.HouseBlocks.FirstOrDefaultAsync(b => b.Id == blockId && b.CompanyId == companyId && b.DeletedAtUtc == null, ct);
        if (block is null) return NotFound();
        var comment = StaysSettingsRules.Trim(input.Comment);
        var today = StayTime.LocalDate(company.TimeZoneId, clock.UtcNow);
        var error = HouseBlockWriter.Validate(input.StartDate, input.EndDate, comment, today, previousStart: block.StartDate);
        if (error is not null) return BadRequest(error);
        if (!Enum.IsDefined(input.Kind)) return BadRequest("Неизвестный тип блокировки");

        var actor = await actors.ResolveStaffAsync(User, ct);
        var result = await blocks.UpdateAsync(company, block, input.StartDate!.Value, input.EndDate!.Value, input.Kind, comment, actor, ct);
        if (result.Error is not null) return result.Error;
        var createdBy = block.CreatedByUserId.Length == 0 ? string.Empty : await actors.NameOfAsync(block.CreatedByUserId, ct) ?? string.Empty;
        return Ok(StaysBoardService.ToDto(block, createdBy));
    }

    [HttpDelete("blocks/{blockId:guid}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> DeleteBlock(Guid companyId, Guid blockId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBlocks, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var block = await db.HouseBlocks.FirstOrDefaultAsync(b => b.Id == blockId && b.CompanyId == companyId && b.DeletedAtUtc == null, ct);
        if (block is null) return NotFound();
        await blocks.DeleteAsync(r.Company!, block, await actors.ResolveStaffAsync(User, ct), ct);
        return NoContent();
    }

    [HttpGet("schedule")]
    [EnableRateLimiting("stays-board")]
    public async Task<ActionResult<StaysScheduleDto>> Schedule(Guid companyId, [FromQuery] string? from, [FromQuery] int? days, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewSchedule, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        DateOnly? fromDate = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!StaysCatalogService.TryDate(from, out var parsed)) return BadRequest("Неверный формат даты");
            fromDate = parsed;
        }
        return Ok(await schedule.BuildAsync(r.Company!, r.Role, fromDate, days, ct));
    }
}
