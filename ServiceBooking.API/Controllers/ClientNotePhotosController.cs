using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// The four photo endpoints for client notes (US-17..US-20). All four live under one prefix because the
/// serving path is fixed dossier-wide by SPEC US-19 p.4 — API_CONTRACT.md §4-6.
/// </summary>
[ApiController]
[Route("api/client-notes")]
public class ClientNotePhotosController(
    AppDbContext db, ImageUploadService imageUploadService, FileStorage storage, SubscriptionResolver subscriptionResolver,
    ConsentLedger ledger)
    : ControllerBase
{
    private const int MaxPhotosPerNote = 5;

    [HttpPost("{noteId:guid}/photos")]
    [Authorize]
    [RequiresOwnerTerms]
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

        // T5-B7 (ARCHITECTURE_CYCLE5.md §44.3, API_CONTRACT_CYCLE5.md §44.3): blocks ONLY the upload —
        // the note itself, the booking, and every other interaction with this client keep working
        // without any consent at all (US-76 п. 6). Salon-scoped, phone-keyed — same reasoning as
        // ClientConsentsController's own subject resolution: a client's identity inside one company's
        // data is their phone, whether or not they have an account.
        //
        // Code review В2: the gate must default to "cannot confirm — do not allow", not fail-open.
        // hasSubjectIdentity/ResolveSubjectPhoneAsync are deliberately split: a note with NEITHER
        // ClientId NOR GuestPhone genuinely has no one to ask (MastersController.AddNote's own
        // validation makes this unreachable in practice, kept as a defensive no-op) — but a note that
        // DOES name a client, whose phone fails to resolve (an AppUser.PhoneNumber cleared by account
        // anonymization is the concrete case that was previously fail-open here), must BLOCK, not skip:
        // there is no canonical phone left to check consent against, which is exactly the state consent
        // cannot be confirmed in.
        var hasSubjectIdentity = !string.IsNullOrEmpty(note.GuestPhone) || !string.IsNullOrEmpty(note.ClientId);
        if (hasSubjectIdentity)
        {
            var subjectPhone = await ResolveSubjectPhoneAsync(note);
            if (subjectPhone is null)
                return BadRequest("Не удалось подтвердить согласие клиента на фотофиксацию.");

            var subject = ConsentSubject.ForPhoneInCompany(subjectPhone, note.CompanyId);
            var consent = await ledger.CurrentAsync(subject, LegalTextKey.PhotoConsent, purpose: null);
            if (consent is null)
                return BadRequest("Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.");
        }

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

    /// <summary>The canonical phone identifying this note's client, however they're recorded — null when
    /// there is no phone to resolve at all (an anonymized AppUser's PhoneNumber is cleared, or — the
    /// defensive edge case of a note with neither ClientId nor GuestPhone — MastersController.AddNote's
    /// own validation makes that one unreachable through normal use). Callers must NOT treat a null
    /// result as "nothing to check" once <see cref="ClientNote.ClientId"/>/<see cref="ClientNote.GuestPhone"/>
    /// shows a subject identity exists (code review В2) — only the caller knows whether an identity was
    /// present to begin with. Explicitly re-normalized (code review В2): GuestPhone is written canonical
    /// by MastersController.AddNote and AppUser.PhoneNumber by AuthController today, but consent is READ
    /// back keyed by the canonical form (ClientConsentsController.ResolveClientAsync) — re-normalizing
    /// here removes any dependency on that staying true everywhere phones are ever written.</summary>
    private async Task<string?> ResolveSubjectPhoneAsync(ClientNote note)
    {
        string? rawPhone;
        if (!string.IsNullOrEmpty(note.GuestPhone)) rawPhone = note.GuestPhone;
        else if (!string.IsNullOrEmpty(note.ClientId))
            rawPhone = await db.Users.AsNoTracking().Where(u => u.Id == note.ClientId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        else rawPhone = null;

        if (string.IsNullOrEmpty(rawPhone)) return null;
        return PhoneNormalizer.TryNormalize(rawPhone, out var canonical) ? canonical : null;
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
