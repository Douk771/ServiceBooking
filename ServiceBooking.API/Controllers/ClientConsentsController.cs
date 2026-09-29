using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
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
    AppDbContext db, LegalDocumentProvider legalProvider, ConsentLedger ledger, HealthNoteProtector healthNoteProtector,
    WrittenHealthConsentRevoker writtenHealthConsentRevoker)
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
            : await db.Bookings.AnyAsync(b => b.CompanyId == companyId && b.ClientId == null && b.GuestPhone == phone);  // SUBJECT-PHONE-GATE: staff-scoped — company staff resolving a client WITHIN their own company by a key staff itself supplied; not an account-scoped "my own data" query (ARCHITECTURE_CYCLE16.md §245.3)
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

    // ── Health note (US-77, T5-B6; cycle 20 US-20-01 — gated ONLY by the paper written-consent mark) ──

    [HttpGet("health-note")]
    public async Task<IActionResult> GetHealthNote(Guid companyId, string clientKey)
    {
        // §48.2 rule 2: SuperAdmin is refused outright, not given an empty value — the precedent is
        // ClientNotePhotosController.ServeAsync, the only other place this happens.
        if (User.IsInRole("SuperAdmin")) return Forbid();
        if (!await IsStaffAsync(companyId)) return Forbid();

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        var formText = legalProvider.Current?.GetText(LegalTextKey.HealthDataWrittenConsentForm);
        if (formText is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        var writtenState = await CurrentWrittenConsentAsync(companyId, resolved.Value);
        var writtenConsent = await BuildWrittenConsentStateDtoAsync(writtenState, formText.Version);
        var hasConsent = writtenState is not null;

        var row = await FindHealthNoteRowAsync(companyId, resolved.Value);
        if (row is null)
            return Ok(new HealthNoteDto(null, null, null, ConsentRequired: !hasConsent, writtenConsent));

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
            return Ok(new HealthNoteDto("(данные недоступны, обратитесь к платформе)", row.UpdatedAt, updatedByName, ConsentRequired: false, writtenConsent));

        return Ok(new HealthNoteDto(value, row.UpdatedAt, updatedByName, ConsentRequired: false, writtenConsent));
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

        // ARCHITECTURE_CYCLE20.md §402.3 (US-20-01) — ONLY the paper written-consent mark satisfies this
        // gate from now on; the salon-scoped electronic HealthDataConsent form and the account holder's
        // own PdnConsent/HealthData purpose no longer count.
        if (await CurrentWrittenConsentAsync(companyId, resolved.Value) is null)
            return BadRequest(new RequiredConsentDto(
                WrittenHealthConsentTexts.ConfirmationRequiredMessage, LegalTextKey.HealthDataWrittenConsentForm));

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

    // ARCHITECTURE_CYCLE20.md §402.3, API_CONTRACT_CYCLE20.md §432.7 (US-20-01, LG1) — the salon's
    // electronic health consent is withdrawn wholesale, replaced by the paper-form endpoints below. No
    // rights check runs before the 410 — the route is retired outright, same convention as a cycle-7
    // sunset route.
    [HttpPost("health-consent")]
    public IActionResult PostHealthConsentGone(Guid companyId, string clientKey) =>
        StatusCode(StatusCodes.Status410Gone, WrittenHealthConsentTexts.LegacyElectronicConsentGoneMessage);

    // ── Written health consent (paper form) — US-20-01, §402.5/§402.4 ───────────────────────────────

    [HttpGet("health-consent-form")]
    public async Task<IActionResult> GetHealthConsentForm(Guid companyId, string clientKey)
    {
        if (User.IsInRole("SuperAdmin")) return Forbid();
        if (!await IsStaffAsync(companyId)) return Forbid();

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        var text = legalProvider.Current?.GetText(LegalTextKey.HealthDataWrittenConsentForm);
        if (text is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        string? clientFullName;
        if (resolved.Value.UserId is not null)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == resolved.Value.UserId);
            clientFullName = user is null ? null : $"{user.FirstName} {user.LastName}".Trim();
        }
        else
        {
            // §402.5: "гость — GuestName последней записи в этой компании" — same "last booking" ordering
            // convention as MastersController/ProfileController (Date desc, then StartTime desc).
            clientFullName = await db.Bookings.AsNoTracking()
                .Where(b => b.CompanyId == companyId && b.ClientId == null && b.GuestPhone == resolved.Value.Phone)  // SUBJECT-PHONE-GATE: staff-scoped — company staff resolving a client WITHIN their own company by a key staff itself supplied (ARCHITECTURE_CYCLE16.md §245.3), same reasoning as ResolveClientAsync above
                .OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                .Select(b => b.GuestName)
                .FirstOrDefaultAsync();
        }

        BillingAccount? billingAccount = company.BillingAccountId is null
            ? null
            : await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == company.BillingAccountId);

        var formId = HealthConsentFormId.New();
        var formPrintedDateMsk = PlatformNoticeRules.TodayMoscow(DateTime.UtcNow);

        var runtimeValues = new HealthConsentFormRuntimeValuesDto(
            string.IsNullOrWhiteSpace(clientFullName) ? null : clientFullName,
            company.Name, company.Address,
            billingAccount?.ConsentOperatorFullName, billingAccount?.ConsentOperatorAddress, billingAccount?.ConsentOperatorInn,
            formId, formPrintedDateMsk.ToString("dd.MM.yyyy"));

        var operatorDetailsMissing = ConsentOperatorDetailsValidator.IsMissing(
            billingAccount?.ConsentOperatorFullName, billingAccount?.ConsentOperatorAddress);

        Response.Headers.CacheControl = "no-store";
        return Ok(new HealthConsentFormDto(
            LegalTextKey.HealthDataWrittenConsentForm, text.Version, formId, formPrintedDateMsk, runtimeValues, operatorDetailsMissing));
    }

    [HttpPost("health-written-consent")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> PostHealthWrittenConsent(Guid companyId, string clientKey, [FromBody] WrittenHealthConsentInputDto dto)
    {
        if (User.IsInRole("SuperAdmin")) return Forbid();
        if (!await IsStaffAsync(companyId)) return Forbid();

        if (!dto.Confirmed) return BadRequest("Подтверждение обязательно.");
        if (dto.FormId is not null && !HealthConsentFormId.IsValid(dto.FormId))
            return BadRequest("Неверный формат номера бланка.");

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        var text = legalProvider.Current?.GetText(LegalTextKey.HealthDataWrittenConsentForm);
        if (text is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
        if (dto.TextVersion != text.Version)
            return Conflict(WrittenHealthConsentTexts.TextVersionOutdatedMessage);

        var subject = ConsentSubject.ForPhoneInCompany(resolved.Value.Phone, companyId);

        // §402.2's own idempotency rule — a LIVE mark with the SAME FormId and the SAME DocumentVersion
        // is a double-click/retry, not a new event, regardless of how long ago it was recorded (this is
        // deliberately NOT the generic 5-second window ConsentLedger.GrantAsync applies to every other
        // document key).
        var existing = await ledger.CurrentAsync(subject, LegalTextKey.HealthDataWrittenConsentForm, purpose: null);
        if (existing is not null && existing.FormId == dto.FormId && existing.DocumentVersion == text.Version)
            return Ok(await BuildWrittenConsentStateDtoAsync(existing, text.Version));

        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await ledger.GrantAsync(new ConsentGrant(
            subject, LegalTextKey.HealthDataWrittenConsentForm, text.Version, text.ContentHash, Purpose: null,
            ConsentAct.Confirmed, ConsentSource.PaperForm, IpAddress: null, UserAgent: null,
            RecordedByUserId: staffUserId, FormId: dto.FormId));

        var state = await ledger.CurrentAsync(subject, LegalTextKey.HealthDataWrittenConsentForm, purpose: null);
        return Ok(await BuildWrittenConsentStateDtoAsync(state, text.Version));
    }

    [HttpPost("health-written-consent/revoke")]
    public async Task<IActionResult> RevokeHealthWrittenConsent(Guid companyId, string clientKey, [FromBody] WrittenHealthConsentRevokeInputDto dto)
    {
        if (User.IsInRole("SuperAdmin")) return Forbid();
        if (!await IsStaffAsync(companyId)) return Forbid();

        var reasonText = dto.Reason switch
        {
            "SubjectWithdrew" => WrittenHealthConsentTexts.RevokedByStaffSubjectWithdrew,
            "MarkedByMistake" => WrittenHealthConsentTexts.RevokedByStaffMarkedByMistake,
            _ => (string?)null,
        };
        if (reasonText is null) return BadRequest($"Неизвестное значение reason '{dto.Reason}'.");

        var resolved = await ResolveClientAsync(companyId, clientKey);
        if (resolved is null) return NotFound();

        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await writtenHealthConsentRevoker.RevokeAsync(
            resolved.Value.Phone, companyId, reasonText, staffUserId, resolved.Value.UserId);

        var text = legalProvider.Current?.GetText(LegalTextKey.HealthDataWrittenConsentForm);
        var currentFormVersion = text?.Version ?? "";
        var writtenConsent = new WrittenHealthConsentStateDto(false, null, null, null, null, null, currentFormVersion);

        return Ok(new WrittenHealthConsentRevokeResultDto(result.Revoked, result.HealthNotesDeleted, writtenConsent));
    }

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
            : db.ClientHealthNotes.FirstOrDefaultAsync(n => n.CompanyId == companyId && n.GuestPhone == resolved.Phone);  // SUBJECT-PHONE-GATE: staff-scoped — company staff resolving THEIR OWN company's client, key already validated by ResolveClientAsync (ARCHITECTURE_CYCLE16.md §245.3)

    /// <summary>ARCHITECTURE_CYCLE20.md §402.3 (US-20-01) — replaces the old <c>HasHealthConsentAsync</c>:
    /// only the paper written-consent mark (Source = PaperForm) counts from now on. The salon-scoped
    /// electronic HealthDataConsent form and the account holder's own PdnConsent/HealthData purpose no
    /// longer satisfy this gate.</summary>
    private Task<ConsentState?> CurrentWrittenConsentAsync(Guid companyId, ResolvedClient resolved)
    {
        var subject = ConsentSubject.ForPhoneInCompany(resolved.Phone, companyId);
        return ledger.CurrentAsync(subject, LegalTextKey.HealthDataWrittenConsentForm, purpose: null);
    }

    /// <summary>§432.1's <c>writtenConsent</c> object — present in EVERY response, `granted: false` with
    /// every field but <c>currentFormVersion</c> null when there is no live mark.</summary>
    private async Task<WrittenHealthConsentStateDto> BuildWrittenConsentStateDtoAsync(ConsentState? state, string currentFormVersion)
    {
        if (state is null) return new WrittenHealthConsentStateDto(false, null, null, null, null, null, currentFormVersion);

        string? confirmedByName = null;
        var recordedByUserId = await db.ConsentRecords.AsNoTracking()
            .Where(r => r.Id == state.Id).Select(r => r.RecordedByUserId).FirstOrDefaultAsync();
        if (recordedByUserId is not null)
        {
            var staff = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == recordedByUserId);
            confirmedByName = staff is not null ? $"{staff.FirstName} {staff.LastName}".Trim() : null;
        }

        return new WrittenHealthConsentStateDto(
            true, state.Id, state.GrantedAtUtc, confirmedByName, state.FormId, state.DocumentVersion, currentFormVersion);
    }

    private static string SubjectKey(ResolvedClient resolved) => resolved.UserId ?? $"phone:{resolved.Phone}";
}

public record SalonConsentDto(bool Granted, DateTime? GrantedAt, string? Version, string? ConfirmedBy, bool TextVersionOutdated, string? Source);
public record SubmitSalonConsentDto(string TextVersion, bool Confirmed);
public record HealthNoteDto(string? Value, DateTime? UpdatedAt, string? UpdatedBy, bool ConsentRequired, WrittenHealthConsentStateDto WrittenConsent);
public record UpdateHealthNoteDto(string? Value);
public record RequiredConsentDto(string Message, string RequiredTextKey);

// ARCHITECTURE_CYCLE20.md §402.5/§402.4, API_CONTRACT_CYCLE20.md §432.4-§432.6 (US-20-01, NEW).
public record WrittenHealthConsentStateDto(
    bool Granted, Guid? RecordId, DateTime? ConfirmedAt, string? ConfirmedByName, string? FormId, string? FormVersion, string CurrentFormVersion);

public record HealthConsentFormRuntimeValuesDto(
    string? ClientFullName, string? CompanyName, string? CompanyAddress,
    string? OperatorFullName, string? OperatorAddress, string? OperatorInn, string FormId, string FormPrintedDate);

public record HealthConsentFormDto(
    string TextKey, string TextVersion, string FormId, DateOnly FormPrintedDate,
    HealthConsentFormRuntimeValuesDto RuntimeValues, bool OperatorDetailsMissing);

public record WrittenHealthConsentInputDto(string TextVersion, string? FormId, bool Confirmed);
public record WrittenHealthConsentRevokeInputDto(string Reason);
public record WrittenHealthConsentRevokeResultDto(int Revoked, int HealthNotesDeleted, WrittenHealthConsentStateDto WrittenConsent);
