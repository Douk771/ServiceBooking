using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServicesController(AppDbContext db, ImageUploadService imageUploadService, FileStorage storage) : ControllerBase
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
            Price = dto.Price
            // ImageUrl is set only by POST /api/services/{id}/image — see CreateServiceDto's doc comment.
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
        // ImageUrl is deliberately left untouched here — only POST /api/services/{id}/image may change it.

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

    // US-25: right to set a service's picture is the same predicate as the right to edit the service
    // itself (CompanyOwner or SuperAdmin — a Master reads services but doesn't manage the catalog,
    // decision Q1 from cycle A). Public storage class: the picture is shown on the storefront to anyone,
    // so it doesn't go through the private client-photo pipeline. Not counted against the client-photo
    // quota (US-25 p.8) — one file per service plus the 5 MB size limit is the only cap here.
    [HttpPost("{id:guid}/image")]
    [Authorize]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ServiceDto>> UploadImage(Guid id, IFormFile? file)
    {
        // Existence is checked before rights, matching every other method in this controller.
        var service = await db.Services.FindAsync(id);
        if (service is null) return NotFound();
        if (!await CanManageCompany(service.CompanyId)) return Forbid();

        var validation = await imageUploadService.ReadAndProcessAsync(file, ImageProfile.ServiceImage);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);

        // Order matters (ARCHITECTURE.md §1.4/§21.2, code review finding): write the new file, commit the
        // new URL, THEN delete the old file. A failed SaveChangesAsync after deleting the old file first
        // would leave a live row pointing at nothing — the one state this pipeline must never produce.
        var oldUrl = service.ImageUrl;
        var newUrl = await storage.SavePublicAsync(PublicArea.Services, validation.Image!.Bytes, validation.Image.Extension);
        service.ImageUrl = newUrl;
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            storage.DeletePublic(newUrl); // the update didn't persist — don't leave the new file orphaned either
            throw;
        }

        storage.DeletePublic(oldUrl); // old file removed on replace, only after the swap committed

        return Ok(new ServiceDto(service.Id, service.CompanyId, service.Name, service.Description, service.DurationMinutes, service.Price, service.ImageUrl));
    }

    private async Task<bool> CanManageCompany(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        if (User.IsInRole("SuperAdmin")) return true;

        // Only the CompanyOwner may create/edit/delete services now (US-09, decision Q1) — a Master
        // reads services (GET stays public) but no longer manages the catalog.
        return await CompanyMembership.IsOwnerAsync(db, companyId, userId);
    }
}
