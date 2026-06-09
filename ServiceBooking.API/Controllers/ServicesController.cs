using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServicesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ServiceDto>>> GetByCompany([FromQuery] Guid companyId)
    {
        var services = await db.Services
            .Where(s => s.CompanyId == companyId && s.IsActive)
            .Select(s => new ServiceDto(s.Id, s.CompanyId, s.Name, s.Description, s.DurationMinutes, s.Price, s.ImageUrl))
            .ToListAsync();

        return Ok(services);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ServiceDto>> Create(CreateServiceDto dto)
    {
        if (!await CanManageCompany(dto.CompanyId)) return Forbid();

        var service = new Service
        {
            Id = Guid.NewGuid(),
            CompanyId = dto.CompanyId,
            Name = dto.Name,
            Description = dto.Description,
            DurationMinutes = dto.DurationMinutes,
            Price = dto.Price,
            ImageUrl = dto.ImageUrl
        };

        db.Services.Add(service);
        await db.SaveChangesAsync();

        return Ok(new ServiceDto(service.Id, service.CompanyId, service.Name, service.Description, service.DurationMinutes, service.Price, service.ImageUrl));
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<ServiceDto>> Update(Guid id, CreateServiceDto dto)
    {
        var service = await db.Services.FindAsync(id);
        if (service is null) return NotFound();
        if (!await CanManageCompany(service.CompanyId)) return Forbid();

        service.Name = dto.Name;
        service.Description = dto.Description;
        service.DurationMinutes = dto.DurationMinutes;
        service.Price = dto.Price;
        if (dto.ImageUrl is not null) service.ImageUrl = dto.ImageUrl;

        await db.SaveChangesAsync();

        return Ok(new ServiceDto(service.Id, service.CompanyId, service.Name, service.Description, service.DurationMinutes, service.Price, service.ImageUrl));
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var service = await db.Services.FindAsync(id);
        if (service is null) return NotFound();
        if (!await CanManageCompany(service.CompanyId)) return Forbid();

        service.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> CanManageCompany(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        if (User.IsInRole("SuperAdmin")) return true;

        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == companyId &&
            cm.UserId == userId &&
            (cm.Role == UserRole.CompanyOwner || cm.Role == UserRole.Master));
    }
}
