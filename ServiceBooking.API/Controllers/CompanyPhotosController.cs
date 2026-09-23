using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Showcase photo gallery for a company (ARCHITECTURE_CYCLE10.md §106, API_CONTRACT_CYCLE10.md §125-128,
/// US-125/US-126). Public storage class — same conveyor as the company logo
/// (<see cref="CompaniesController.UploadLogo"/>), new profiles only (ImageProfile.CompanyPhoto/
/// CompanyPhotoThumb). Deliberately NOT the same controller as CompaniesController: this is a self-
/// contained sub-resource with its own four routes, matching the ClientNotePhotosController precedent.
///
/// Rights (§106): only the company's OWNER (CompanyMembership.IsOwnerAsync) or SuperAdmin — a master
/// never manages the gallery, unlike ClientNotePhotos, where any staff member may.
/// No PhotoQuotaMb accounting, no ClientNotePhotoDays/photo-retention-cleanup participation, no
/// ConsentRecord write anywhere in this file (decisions П5/П7 — deliberately absent, not merely unused).
/// </summary>
[ApiController]
[Route("api/companies")]
public class CompanyPhotosController(AppDbContext db, ImageUploadService imageUploadService, FileStorage storage)
    : ControllerBase
{
    [HttpGet("{id:guid}/photos")]
    public async Task<ActionResult<List<CompanyPhotoDto>>> GetPhotos(Guid id)
    {
        var exists = await db.Companies.AnyAsync(c => c.Id == id);
        if (!exists) return NotFound();

        var photos = await db.CompanyPhotos
            .Where(p => p.CompanyId == id)
            .OrderBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToListAsync();

        return Ok(photos.Select(CompanyPhotoDto.From).ToList());
    }

    [HttpPost("{id:guid}/photos")]
    [Authorize]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<CompanyPhotoDto>> Upload(Guid id, IFormFile? file)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await IsOwnerOrSuperAdmin(id)) return Forbid();

        var validation = await imageUploadService.ReadAndProcessAsync(
            file, [ImageProfile.CompanyPhoto, ImageProfile.CompanyPhotoThumb]);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);

        var full = validation.Images![0];
        var thumb = validation.Images![1];
        // Hash of the PROCESSED full-size bytes — same dedup-by-hash convention as ClientNotePhoto.
        var contentHash = Convert.ToHexString(SHA256.HashData(full.Bytes)).ToLowerInvariant();

        // Cheap early return for the common double-click/retry case, before touching the lock;
        // re-checked below, inside the lock, to close the race against a genuinely concurrent identical
        // upload (same pattern as ClientNotePhotosController.Upload).
        var existing = await db.CompanyPhotos.FirstOrDefaultAsync(p => p.CompanyId == id && p.ContentHash == contentHash);
        if (existing is not null) return Ok(CompanyPhotoDto.From(existing));

        await using var tx = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-photos:{id}");

        existing = await db.CompanyPhotos.FirstOrDefaultAsync(p => p.CompanyId == id && p.ContentHash == contentHash);
        if (existing is not null)
        {
            await tx.RollbackAsync();
            return Ok(CompanyPhotoDto.From(existing));
        }

        var photoCount = await db.CompanyPhotos.CountAsync(p => p.CompanyId == id);
        if (photoCount >= CompanyPhotoOrdering.MaxPhotosPerCompany)
        {
            await tx.RollbackAsync();
            return BadRequest("В галерее салона может быть не больше 10 фотографий");
        }

        // §127: written to disk BEFORE the row is committed — if SaveChangesAsync fails, the new files
        // are deleted so nothing is orphaned (same ordering as UploadLogo).
        var url = await storage.SavePublicAsync(PublicArea.Companies, full.Bytes, full.Extension);
        var thumbnailUrl = await storage.SavePublicAsync(PublicArea.Companies, thumb.Bytes, thumb.Extension);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var photo = new CompanyPhoto
        {
            Id = Guid.NewGuid(),
            CompanyId = id,
            Url = url,
            ThumbnailUrl = thumbnailUrl,
            ContentType = full.ContentType,
            SizeBytes = full.Bytes.LongLength + thumb.Bytes.LongLength,
            Width = full.Width,
            Height = full.Height,
            ContentHash = contentHash,
            Position = photoCount, // §127: appended at the end, never becomes the cover on its own
            UploadedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.CompanyPhotos.Add(photo);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            storage.DeletePublic(url);
            storage.DeletePublic(thumbnailUrl);
            throw;
        }

        await tx.CommitAsync();
        return Created($"/api/companies/{id}/photos/{photo.Id}", CompanyPhotoDto.From(photo));
    }

    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id, Guid photoId)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await IsOwnerOrSuperAdmin(id)) return Forbid();

        var photo = await db.CompanyPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.CompanyId == id);
        // A photoId belonging to a DIFFERENT company is indistinguishable from "doesn't exist" —
        // API_CONTRACT_CYCLE10.md §128.1: existence of someone else's photo is never confirmed.
        if (photo is null) return NotFound();

        await using var tx = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-photos:{id}");

        var (url, thumbnailUrl) = (photo.Url, photo.ThumbnailUrl);

        // Row first (§1.4 convention): commit removes the row and renumbers the rest in ONE transaction,
        // file cleanup happens only after that commit succeeds.
        db.CompanyPhotos.Remove(photo);
        var remaining = await db.CompanyPhotos
            .Where(p => p.CompanyId == id && p.Id != photoId)
            .ToListAsync();
        CompanyPhotoOrdering.Compact(remaining);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        storage.DeletePublic(url);
        storage.DeletePublic(thumbnailUrl);

        return NoContent();
    }

    [HttpPut("{id:guid}/photos/order")]
    [Authorize]
    public async Task<ActionResult<List<CompanyPhotoDto>>> Reorder(Guid id, ReorderCompanyPhotosDto dto)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await IsOwnerOrSuperAdmin(id)) return Forbid();

        await using var tx = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-photos:{id}");

        var current = await db.CompanyPhotos.Where(p => p.CompanyId == id).ToListAsync();
        try
        {
            CompanyPhotoOrdering.ApplyOrder(current, dto.PhotoIds);
        }
        catch (InvalidPhotoReorderException ex)
        {
            await tx.RollbackAsync();
            return BadRequest(ex.Message);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var ordered = current.OrderBy(p => p.Position).ToList();
        return Ok(ordered.Select(CompanyPhotoDto.From).ToList());
    }

    private async Task<bool> IsOwnerOrSuperAdmin(Guid companyId)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is not null && await CompanyMembership.IsOwnerAsync(db, companyId, userId);
    }
}
