using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Salon-facing consent and health-note endpoints for one client of one company — US-76 (photo
/// consent, T5-B7) and US-77 (health note, T5-B6), ARCHITECTURE_CYCLE5.md §44.2–§44.3, §48.
///
/// Both consent kinds (photo, health) are recorded against the SAME journal (ConsentRecord) as
/// everything else in §44.2 — `DocumentKey` is one of the two `LegalTextKey` interface-text keys these
/// forms present, not a `LegalDocumentType`: neither ever gates access anywhere (§43.1's "никогда").
///
/// Every subject here is resolved to a canonical PHONE, even when `{clientKey}` is a registered client's
/// userId (ClientKey.IsPhone == false) — ConsentRecord's salon-scoped partial index is
/// (SubjectPhone, CompanyId, ...), not (UserId, CompanyId, ...): a client's identity inside ONE salon's
/// data is, structurally, their phone number, whether or not they happen to have an account
/// (ARCHITECTURE_CYCLE5.md §45.1, ConsentSubject.ForPhoneInCompany's own doc comment). ClientHealthNote's
/// own storage row, in contrast, mirrors ClientNote's existing ClientId/GuestPhone split exactly — the
/// two concerns (consent journal indexing vs. operational note storage) are allowed to key differently.
/// </summary>
[ApiController]
[Route("api/companies/{companyId:guid}/clients/{clientKey}")]
[Authorize]
public class ClientConsentsController(
    AppDbContext db, LegalDocumentProvider legalProvider, ConsentLedger ledger, HealthNoteProtector healthNoteProtector)
    : ControllerBase
{
    // ── Resolution shared by every endpoint below ───────────────────────────────────────────────────

    private readonly record struct ResolvedClient(string? UserId, string Phone);

    /// <summary>Null means: malformed key, or a client genuinely unrelated to this company — callers
    /// answer 404 either way, existence of someone else's client is never distinguished from "bad key"
    /// (same "don't confirm what you don't need to" reasoning as ClientNotePhotosController's 404s).</summary>
    private async Task<ResolvedClient?> ResolveClientAsync(Guid companyId, string clientKey)
    {
        string? userId;
        string phone;

        if (ClientKey.IsPhone(clientKey))
        {
            var rawPhone = ClientKey.ExtractPhone(clientKey);
            if (!PhoneNormalizer.TryNormalize(rawPhone, out var canonicalPhone)) return null;
            userId = null;
            phone = canonicalPhone;
        }
        else
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == clientKey);
            if (user?.PhoneNumber is null) return null;
            userId = user.Id;
            phone = user.PhoneNumber;
        }

        // "Associated with this company" mirrors MastersController.AddNote's own check: at least one
        // booking here, by client id or by canonical guest phone (a client who only ever left a note or
        // photo without ever booking cannot exist — both require a note, which requires a booking-derived
        // client key in the first place, per MastersController's own validation).
        var hasBookingHere = userId is not null
            ? await db.Bookings.AnyAsync(b => b.CompanyId == companyId && b.ClientId == userId)
            : await db.Bookings.AnyAsync(b => b.CompanyId == companyId && b.ClientId == null && b.GuestPhone == phone);
        if (!hasBookingHere) return null;

        return new ResolvedClient(userId, phone);
    }

    private async Task<bool> IsStaffAsync(Guid companyId) =>
        await CompanyMembership.IsStaffAsync(db, companyId, User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── Photo consent (US-76, T5-B7) ────────────────────────────────────────────────────────────────

    [HttpGet("photo-consent")]
    public async Task<ActionResult<SalonConsentDto>> GetPhotoConsent(Guid companyId, string clientKey) =>
        await GetSalonConsentAsync(companyId, clientKey, LegalTextKey.PhotoConsent);

    [HttpPost("photo-consent")]
    public async Task<ActionResult<SalonConsentDto>> PostPhotoConsent(Guid companyId, string clientKey, [FromBody] SubmitSalonConsentDto dto) =>
        await PostSalonConsentAsync(companyId, clientKey, LegalTextKey.PhotoConsent, ConsentSource.PhotoForm, dto);

    // ── Health note (US-77, T5-B6) ──────────────────────────────────────────────────────────────────

    [HttpGet("health-note")]
    public async Task<IActionResult> GetHealthNote(Guid companyId, string clientKey)
    {
        // §48.2 rule 2: SuperAdmin is refused outright, not given an empty value — the precedent is
        // ClientNotePhotosController.ServeAsync, the only other place this happens.
        if (User.IsInRole("SuperAdmin")) return Forbid();
        if (!await IsStaffAsync(companyId)) return Forbid();

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        var row = await FindHealthNoteRowAsync(companyId, resolved.Value);
        if (row is null)
        {
            var hasConsent = await HasHealthConsentAsync(companyId, resolved.Value);
            return Ok(new HealthNoteDto(null, null, null, ConsentRequired: !hasConsent));
        }

        var value = healthNoteProtector.Unprotect(row.Ciphertext, companyId, SubjectKey(resolved.Value));
        string? updatedByName = null;
        if (row.UpdatedByUserId is not null)
        {
            var updatedBy = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UpdatedByUserId);
            updatedByName = updatedBy is not null ? $"{updatedBy.FirstName} {updatedBy.LastName}".Trim() : null;
        }

        // A decrypt failure (rotated/lost key, corrupted row — never expected in normal operation) must
        // not read as "no note was ever written"; §48.1 promises a distinct, honest answer instead.
        if (value is null)
            return Ok(new HealthNoteDto("(данные недоступны, обратитесь к платформе)", row.UpdatedAt, updatedByName, ConsentRequired: false));

        return Ok(new HealthNoteDto(value, row.UpdatedAt, updatedByName, ConsentRequired: false));
    }

    [HttpPut("health-note")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> PutHealthNote(Guid companyId, string clientKey, [FromBody] UpdateHealthNoteDto dto)
    {
        if (User.IsInRole("SuperAdmin")) return Forbid();
        if (!await IsStaffAsync(companyId)) return Forbid();

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        if (dto.Value is not { Length: > 0 })
            return BadRequest("Значение не может быть пустым.");
        if (dto.Value.Length > 2000)
            return BadRequest("Значение не должно превышать 2000 символов.");

        // §45.2 / §48.3: blocks ONLY this field's write — the salon-scoped HealthDataConsent form, OR
        // the account holder's own PdnConsent/HealthData purpose, either satisfies it.
        if (!await HasHealthConsentAsync(companyId, resolved.Value))
            return BadRequest(new RequiredConsentDto(
                "Для заполнения этого поля нужно согласие клиента на обработку сведений о состоянии здоровья.",
                LegalTextKey.HealthDataConsent));

        var subjectKey = SubjectKey(resolved.Value);
        var ciphertext = healthNoteProtector.Protect(dto.Value, companyId, subjectKey);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var row = await FindHealthNoteRowAsync(companyId, resolved.Value);
        if (row is null)
        {
            row = new ClientHealthNote
            {
                Id = Guid.NewGuid(), CompanyId = companyId,
                ClientId = resolved.Value.UserId, GuestPhone = resolved.Value.UserId is null ? resolved.Value.Phone : null,
            };
            db.ClientHealthNotes.Add(row);
        }

        row.Ciphertext = ciphertext;
        row.KeyId = healthNoteProtector.CurrentKeyId;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = userId;
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("health-note")]
    public async Task<IActionResult> DeleteHealthNote(Guid companyId, string clientKey)
    {
        if (User.IsInRole("SuperAdmin")) return Forbid();
        if (!await IsStaffAsync(companyId)) return Forbid();

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        // Idempotent — 200 whether or not a row existed (US-77, matches the codebase's other
        // idempotent-delete endpoints, e.g. ConsentLedger.RevokeAsync's "0 revoked is not an error").
        var row = await FindHealthNoteRowAsync(companyId, resolved.Value);
        if (row is not null)
        {
            db.ClientHealthNotes.Remove(row);
            await db.SaveChangesAsync();
        }

        return Ok();
    }

    [HttpPost("health-consent")]
    public async Task<ActionResult<SalonConsentDto>> PostHealthConsent(Guid companyId, string clientKey, [FromBody] SubmitSalonConsentDto dto) =>
        await PostSalonConsentAsync(companyId, clientKey, LegalTextKey.HealthDataConsent, ConsentSource.HealthForm, dto);

    // ── Shared plumbing ──────────────────────────────────────────────────────────────────────────────

    private async Task<ActionResult<SalonConsentDto>> GetSalonConsentAsync(Guid companyId, string clientKey, string documentKey)
    {
        if (!await IsStaffAsync(companyId)) return Forbid();

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        var text = legalProvider.Current?.GetText(documentKey);
        if (text is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        var subject = ConsentSubject.ForPhoneInCompany(resolved.Value.Phone, companyId);
        var state = await ledger.CurrentAsync(subject, documentKey, purpose: null);
        return Ok(await BuildSalonConsentDtoAsync(state, text.Version));
    }

    private async Task<ActionResult<SalonConsentDto>> PostSalonConsentAsync(
        Guid companyId, string clientKey, string documentKey, ConsentSource source, SubmitSalonConsentDto dto)
    {
        if (!await IsStaffAsync(companyId)) return Forbid();
        if (!dto.Confirmed) return BadRequest("Подтверждение обязательно.");

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        var text = legalProvider.Current?.GetText(documentKey);
        if (text is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
        if (dto.TextVersion != text.Version) return Conflict("Текст был обновлён ещё раз — перечитайте и подтвердите заново.");

        var subject = ConsentSubject.ForPhoneInCompany(resolved.Value.Phone, companyId);
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await ledger.GrantAsync(new ConsentGrant(
            subject, documentKey, text.Version, text.ContentHash, Purpose: null, ConsentAct.Consented, source,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent: Request.Headers.UserAgent.ToString(),
            RecordedByUserId: staffUserId));

        var state = await ledger.CurrentAsync(subject, documentKey, purpose: null);
        return Ok(await BuildSalonConsentDtoAsync(state, text.Version));
    }

    private async Task<SalonConsentDto> BuildSalonConsentDtoAsync(ConsentState? state, string currentTextVersion)
    {
        if (state is null) return new SalonConsentDto(false, null, null, null, false, null);

        string? confirmedByName = null;
        // ConsentState doesn't carry RecordedByUserId (it's a journal-wide projection, ARCHITECTURE_
        // CYCLE5.md §45.1) — read directly, the one extra scalar lookup is the same cost class as
        // MastersController's own per-note uploader-name lookup, not a hot path.
        var recordedByUserId = await db.ConsentRecords.AsNoTracking()
            .Where(r => r.Id == state.Id).Select(r => r.RecordedByUserId).FirstOrDefaultAsync();
        if (recordedByUserId is not null)
        {
            var staff = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == recordedByUserId);
            confirmedByName = staff is not null ? $"{staff.FirstName} {staff.LastName}".Trim() : null;
        }

        return new SalonConsentDto(
            true, state.GrantedAtUtc, state.DocumentVersion, confirmedByName,
            TextVersionOutdated: state.DocumentVersion != currentTextVersion, state.Source.ToString());
    }

    private Task<ClientHealthNote?> FindHealthNoteRowAsync(Guid companyId, ResolvedClient resolved) =>
        resolved.UserId is not null
            ? db.ClientHealthNotes.FirstOrDefaultAsync(n => n.CompanyId == companyId && n.ClientId == resolved.UserId)
            : db.ClientHealthNotes.FirstOrDefaultAsync(n => n.CompanyId == companyId && n.GuestPhone == resolved.Phone);

    /// <summary>Consent for THIS field is satisfied by either the salon-scoped HealthDataConsent form
    /// (recorded here, by a staff member, phone+company-keyed) OR the account holder's own
    /// PdnConsent/HealthData purpose grant (recorded in their profile, user-keyed) — §48.3: "либо".</summary>
    private async Task<bool> HasHealthConsentAsync(Guid companyId, ResolvedClient resolved)
    {
        var salonSubject = ConsentSubject.ForPhoneInCompany(resolved.Phone, companyId);
        if (await ledger.CurrentAsync(salonSubject, LegalTextKey.HealthDataConsent, purpose: null) is not null)
            return true;

        if (resolved.UserId is null) return false;
        var accountSubject = ConsentSubject.ForUser(resolved.UserId);
        return await ledger.CurrentAsync(accountSubject, LegalDocumentType.PdnConsent.ToString(), ConsentPurpose.HealthData) is not null;
    }

    private static string SubjectKey(ResolvedClient resolved) => resolved.UserId ?? $"phone:{resolved.Phone}";
}

public record SalonConsentDto(bool Granted, DateTime? GrantedAt, string? Version, string? ConfirmedBy, bool TextVersionOutdated, string? Source);
public record SubmitSalonConsentDto(string TextVersion, bool Confirmed);
public record HealthNoteDto(string? Value, DateTime? UpdatedAt, string? UpdatedBy, bool ConsentRequired);
public record UpdateHealthNoteDto(string? Value);
public record RequiredConsentDto(string Message, string RequiredTextKey);
