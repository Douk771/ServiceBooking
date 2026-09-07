using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// The four photo endpoints for client notes (US-17..US-20). All four live under one prefix because the
/// serving path is fixed dossier-wide by SPEC US-19 p.4 — API_CONTRACT.md §4-6.
/// </summary>
[ApiController]
[Route("api/client-notes")]
public class ClientNotePhotosController(
    AppDbContext db, ImageUploadService imageUploadService, FileStorage storage, SubscriptionResolver subscriptionResolver)
    : ControllerBase
{
    private const int MaxPhotosPerNote = 5;

    [HttpPost("{noteId:guid}/photos")]
    [Authorize]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid noteId, IFormFile? file)
    {
        var note = await db.ClientNotes.FindAsync(noteId);
        if (note is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // The note's company id is already known to the caller from the URL, so there is nothing to
        // hide by distinguishing "not staff here" from "note doesn't exist" — 403, not 404
        // (API_CONTRACT.md §4).
        if (!await CompanyMembership.IsStaffAsync(db, note.CompanyId, userId)) return Forbid();

        var validation = await imageUploadService.ReadAndProcessAsync(
            file, [ImageProfile.ClientNotePhoto, ImageProfile.ClientNotePhotoThumb]);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);

        var full = validation.Images![0];
        var thumb = validation.Images![1];
        // Hash of the PROCESSED bytes, not the upload — see ClientNotePhoto.ContentHash's doc for why.
        var contentHash = Convert.ToHexString(SHA256.HashData(full.Bytes)).ToLowerInvariant();

        // Cheap early return for the common repeat case (double click, retry) before touching the lock;
        // re-checked again below, inside the lock, to close the race against a genuinely concurrent
        // identical upload.
        var existing = await db.ClientNotePhotos.FirstOrDefaultAsync(p => p.ClientNoteId == noteId && p.ContentHash == contentHash);
        if (existing is not null) return Ok(await BuildDtoAsync(existing, note, userId));

        await using var tx = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-photo-quota:{note.CompanyId}");

        existing = await db.ClientNotePhotos.FirstOrDefaultAsync(p => p.ClientNoteId == noteId && p.ContentHash == contentHash);
        if (existing is not null)
        {
            await tx.RollbackAsync();
            return Ok(await BuildDtoAsync(existing, note, userId));
        }

        var photoCount = await db.ClientNotePhotos.CountAsync(p => p.ClientNoteId == noteId);
        if (photoCount >= MaxPhotosPerNote)
        {
            await tx.RollbackAsync();
            return BadRequest("This note already has 5 photos.");
        }

        var totalNewBytes = full.Bytes.LongLength + thumb.Bytes.LongLength;
        var plan = await subscriptionResolver.GetEffectivePlanAsync(note.CompanyId);
        if (plan.PhotoQuotaMb is { } quotaMb)
        {
            var quotaBytes = quotaMb * 1024L * 1024L;
            var usedBytes = await db.ClientNotePhotos
                .Where(p => p.CompanyId == note.CompanyId).SumAsync(p => (long?)p.SizeBytes) ?? 0;
            if (usedBytes + totalNewBytes > quotaBytes)
            {
                await tx.RollbackAsync();
                var usedMb = Math.Round(usedBytes / 1024.0 / 1024.0, 1);
                return BadRequest(
                    $"Photo storage quota exceeded: {usedMb} of {quotaMb} MB used. Upgrade the plan for more space.");
            }
        }

        // Written INSIDE the held lock, deliberately (ARCHITECTURE.md §6.2): releasing the lock before
        // the write reopens the exact quota race the lock exists to close. At 300 KB this costs tens of
        // milliseconds, not a meaningfully longer hold.
        var storagePath = await storage.SavePrivateAsync(note.CompanyId, full.Bytes, full.Extension);
        var thumbnailPath = await storage.SavePrivateAsync(note.CompanyId, thumb.Bytes, thumb.Extension);

        var photo = new ClientNotePhoto
        {
            Id = Guid.NewGuid(),
            ClientNoteId = noteId,
            CompanyId = note.CompanyId,
            StoragePath = storagePath,
            ThumbnailPath = thumbnailPath,
            ContentType = full.ContentType,
            SizeBytes = totalNewBytes,
            Width = full.Width,
            Height = full.Height,
            ContentHash = contentHash,
            UploadedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        db.ClientNotePhotos.Add(photo);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var dto = await BuildDtoAsync(photo, note, userId);
        return Created($"/api/client-notes/photos/{photo.Id}", dto);
    }

    [HttpGet("photos/{id:guid}")]
    [Authorize]
    public Task<IActionResult> GetPhoto(Guid id) => ServeAsync(id, thumbnail: false);

    [HttpGet("photos/{id:guid}/thumb")]
    [Authorize]
    public Task<IActionResult> GetThumbnail(Guid id) => ServeAsync(id, thumbnail: true);

    private async Task<IActionResult> ServeAsync(Guid id, bool thumbnail)
    {
        var photo = await db.ClientNotePhotos.FindAsync(id);
        if (photo is null) return NotFound();

        // Decision Q5: the ONE place in the product where SuperAdmin is refused. Counters and total
        // volume are available through GET /api/companies/{id}/photo-usage; content itself is not.
        if (User.IsInRole("SuperAdmin")) return Forbid();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // Cross-tenant isolation: a caller who isn't staff of THIS photo's company gets 404, the same
        // as an id that doesn't exist at all — existence of someone else's photo is never confirmed.
        if (!await CompanyMembership.IsStaffAsync(db, photo.CompanyId, userId)) return NotFound();

        var key = thumbnail ? photo.ThumbnailPath : photo.StoragePath;
        Stream stream;
        try { stream = storage.OpenPrivate(key); }
        catch (FileNotFoundException) { return NotFound(); }

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(stream, photo.ContentType);
    }

    [HttpDelete("photos/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var photo = await db.ClientNotePhotos.Include(p => p.ClientNote).FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await CompanyMembership.IsStaffAsync(db, photo.CompanyId, userId)) return NotFound();

        // Wider than the letter of Q16 by one person — see ARCHITECTURE.md §21.3: the note's author,
        // whoever actually uploaded THIS photo, or the company's owner.
        var isOwner = await CompanyMembership.IsOwnerAsync(db, photo.CompanyId, userId);
        var canDelete = isOwner || photo.ClientNote.MasterId == userId || photo.UploadedByUserId == userId;
        if (!canDelete) return Forbid();

        var (fullPath, thumbPath) = (photo.StoragePath, photo.ThumbnailPath);

        // Row first (§1.4): the quota aggregate reflects the freed space the instant this commits, no
        // separate recompute step needed (US-20 p.7, ARCHITECTURE.md §6.1/§9.2).
        db.ClientNotePhotos.Remove(photo);
        await db.SaveChangesAsync();

        storage.DeletePrivate(fullPath);
        storage.DeletePrivate(thumbPath);

        return NoContent();
    }

    private async Task<ClientNotePhotoDto> BuildDtoAsync(ClientNotePhoto photo, ClientNote note, string callerId)
    {
        var callerIsOwner = await CompanyMembership.IsOwnerAsync(db, photo.CompanyId, callerId);
        var canDelete = callerIsOwner || note.MasterId == callerId || photo.UploadedByUserId == callerId;

        string? uploadedByName = null;
        if (photo.UploadedByUserId is not null)
        {
            var uploader = await db.Users.FindAsync(photo.UploadedByUserId);
            uploadedByName = uploader is not null ? $"{uploader.FirstName} {uploader.LastName}".Trim() : null;
        }

        return new ClientNotePhotoDto(
            photo.Id, $"/api/client-notes/photos/{photo.Id}", $"/api/client-notes/photos/{photo.Id}/thumb",
            photo.Width, photo.Height, photo.SizeBytes, photo.CreatedAt, uploadedByName, canDelete);
    }
}
