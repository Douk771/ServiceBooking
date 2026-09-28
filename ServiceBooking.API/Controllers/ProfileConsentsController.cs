using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Subjects;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the consent routes of the former <c>ProfileController</c>
/// (<c>api/profile/consents…</c>) — same prefix, same [Authorize], same per-action routes; still
/// allow-listed by path in LegalConsentFilter. The logger keeps the <c>ProfileController</c> category.
/// </summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileConsentsController(
    UserManager<AppUser> userManager, AppDbContext db, FileStorage storage,
    LegalDocumentProvider legalProvider, ConsentLedger ledger,
    SubjectScopeResolver subjectScopeResolver, ILogger<ProfileController> logger)
    : ControllerBase
{
    // ── Consents (ARCHITECTURE_CYCLE5.md §41, API_CONTRACT_CYCLE5.md §41) ──────────────────────────
    //
    // All three are [Authorize] but allow-listed in LegalConsentFilter (a caller blocked by a pending
    // Privacy/TermsClient redaction must still be able to manage the ONE consent that is never itself a
    // reason to block, US-68 p.5). Every read/write here goes through ConsentLedger — this controller
    // never touches ConsentRecords directly, so "what counts as current" is answered in exactly one place.

    [HttpGet("consents")]
    public async Task<ActionResult<ConsentsDto>> GetConsents()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var doc = legalProvider.Current?.Get(LegalDocumentType.PdnConsent);
        if (doc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        return Ok(await BuildConsentsDtoAsync(userId, doc));
    }

    [HttpPost("consents")]
    public async Task<ActionResult<ConsentsDto>> PostConsents([FromBody] SubmitConsentDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // §41.2: this endpoint accepts ONLY PdnConsent — Privacy/TermsClient/TermsOwner go through
        // POST /api/legal/accept, which is what keeps "recorded separately from other documents" a
        // protocol-level guarantee (ARCHITECTURE_CYCLE5.md §46.2) rather than a convention two different
        // request shapes could quietly drift away from.
        if (dto.DocumentKey != LegalDocumentType.PdnConsent.ToString())
            return BadRequest($"Через этот вызов принимается только '{LegalDocumentType.PdnConsent}'.");

        var doc = legalProvider.Current?.Get(LegalDocumentType.PdnConsent);
        if (doc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        if (dto.Version != doc.Version)
            return Conflict("Документ был обновлён ещё раз — перечитайте и подтвердите свой выбор заново.");

        var purposes = dto.Purposes ?? [];
        var parsedPurposes = new List<ConsentPurpose>();
        foreach (var key in purposes)
        {
            if (!Enum.TryParse<ConsentPurpose>(key, ignoreCase: true, out var purpose) || doc.Purposes.All(p => p.Key != purpose))
                return BadRequest($"Неизвестная цель '{key}'.");
            parsedPurposes.Add(purpose);
        }

        var subject = ConsentSubject.ForUser(userId);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        if (parsedPurposes.Count == 0)
        {
            // A valid, deliberate answer: "shown the form, granted nothing" — one row with Purpose: null
            // records that the form was actually presented, distinct from never having called this
            // endpoint at all (API_CONTRACT_CYCLE5.md §41.2).
            await ledger.GrantAsync(new ConsentGrant(
                subject, doc.Type.ToString(), doc.Version, doc.ContentHash, Purpose: null,
                ConsentAct.Consented, ConsentSource.Profile, ipAddress, userAgent));
        }
        else
        {
            foreach (var purpose in parsedPurposes)
            {
                await ledger.GrantAsync(new ConsentGrant(
                    subject, doc.Type.ToString(), doc.Version, doc.ContentHash, purpose,
                    ConsentAct.Consented, ConsentSource.Profile, ipAddress, userAgent));
            }
        }

        return Ok(await BuildConsentsDtoAsync(userId, doc));
    }

    // T5-B5 (ARCHITECTURE_CYCLE5.md §47.1, API_CONTRACT_CYCLE5.md §41.3). Both endpoints share the SAME
    // selection queries (§49.2's "sухой прогон тем же запросом выборки" principle, applied here too,
    // even though this cascade isn't the retention sweep itself) — revoke-preview differs from revoke
    // only in whether SaveChanges/file deletion actually happen.

    [HttpPost("consents/revoke")]
    public async Task<ActionResult<RevokeConsentResponseDto>> RevokeConsent([FromBody] RevokeConsentDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Code review В3: §44.4 describes a revoke mechanism for the SALON-recorded PhotoConsent/
        // HealthDataConsent rows too (ClientConsentsController's PostPhotoConsent/PostHealthConsent) —
        // until now this endpoint only ever accepted PdnConsent, so a person had no way to withdraw a
        // consent a company's staff recorded on their behalf. These two are phone+company scoped
        // (ConsentSubject.ForPhoneInCompany), not user scoped, so a companyId is required to say WHICH
        // salon's record is being withdrawn — deliberately a separate branch from PdnConsent below
        // rather than one that silently reinterprets `purpose`, which has no meaning for these two keys.
        if (dto.DocumentKey is LegalTextKey.PhotoConsent or LegalTextKey.HealthDataConsent)
            return await RevokeSalonConsentAsync(userId, dto);

        if (dto.DocumentKey != LegalDocumentType.PdnConsent.ToString())
            return BadRequest($"Через этот вызов отзывается только '{LegalDocumentType.PdnConsent}', " +
                               $"'{LegalTextKey.PhotoConsent}' или '{LegalTextKey.HealthDataConsent}'.");

        ConsentPurpose? purpose = null;
        if (dto.Purpose is not null)
        {
            if (!Enum.TryParse<ConsentPurpose>(dto.Purpose, ignoreCase: true, out var parsed))
                return BadRequest($"Неизвестная цель '{dto.Purpose}'.");
            purpose = parsed;
        }

        var subject = ConsentSubject.ForUser(userId);

        // §47.3: idempotent — "0 revoked" is a legitimate, non-error outcome (revoking an already-revoked
        // or never-granted purpose), and the cascade below still runs against whatever it finds (which,
        // for an already-clean subject, is nothing — also legitimate, not an error).
        var revoked = await ledger.RevokeAsync(subject, dto.DocumentKey, purpose, dto.Reason);
        var effects = await ApplyOrPreviewRevokeEffectsAsync(userId, purpose, apply: true);

        return Ok(new RevokeConsentResponseDto(revoked, effects));
    }

    /// <summary>The salon-scoped half of RevokeConsent (code review В3) — withdraws a PhotoConsent or
    /// HealthDataConsent recorded by ONE company's staff, and (§44.4: "удаляет фото необратимо" / mirrors
    /// PutHealthNote's own consent-gated write) deletes whatever that consent was covering AT THAT
    /// COMPANY specifically, never account-wide (unlike the PdnConsent WorkPhotos/HealthData purposes,
    /// which are account-wide by construction).</summary>
    private async Task<ActionResult<RevokeConsentResponseDto>> RevokeSalonConsentAsync(string userId, RevokeConsentDto dto)
    {
        if (dto.CompanyId is null || dto.CompanyId == Guid.Empty)
            return BadRequest("Для отзыва согласия, записанного сотрудником компании, укажите companyId.");

        var phone = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        // Н2 (LEGAL_REVIEW_CYCLE16.md §3.4): прежняя строка обещала проверку подтверждения там, где
        // проверялось только НАЛИЧИЕ номера. Теперь два случая вместо одного; формулировки — юриста,
        // команда их не сочиняет. Ни одна не сообщает, существуют ли данные (§245.6 п. 1, правило
        // «ответ не оракул»).
        if (string.IsNullOrEmpty(phone))
            return BadRequest("У учётной записи не указан номер телефона, поэтому отзывать нечего.");

        // TD-03 (ARCHITECTURE_CYCLE16.md §245.2 row 3 — a place the spec itself did not name, found
        // while reading the code). Before this gate, an unverified account could destroy a COMPLETE
        // STRANGER's photos/health notes at any company just by knowing that company's id and typing
        // its own (unproven) phone number. The ledger consent revoke itself stays phone-scoped as
        // before (it is not a destructive read/write of someone else's ClientNote/ClientHealthNote
        // rows); only the two GuestPhone-matched deletions below are gated.
        var user = await userManager.FindByIdAsync(userId);
        var scope = user is null
            ? new SubjectScope(userId, phone, null)
            : await subjectScopeResolver.ForAccountAsync(user, HttpContext.RequestAborted);
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation("guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId, "profile/consents/revoke");
        }

        // 🔴 TD-03-ter (LEGAL_REVIEW_CYCLE16.md находка Н1). Запись в ConsentLedger гейтится ТОЖЕ, а не
        // только удаления ниже. Отзыв чужого согласия — нарушение сам по себе (ч. 3 ст. 9 152-ФЗ), даже
        // когда ни одна строка данных при этом не удалена: согласие субъекта оказывается помечено
        // отозванным по воле постороннего. Ранняя версия цикла 16 оставила этот вызов открытым,
        // рассудив, что он «не разрушительный», — это неверная посылка, и её ловит
        // GuestDataGateCycle16Tests.RevokeSalonConsent_UnconfirmedPhone_CannotRevokeAStranger_SConsent.
        // Свойство обязано держаться ЭТОЙ проверкой, а не порядком строк выше (ранний BadRequest на
        // отсутствующем телефоне спасал лишь случайно и сломался бы при первой перестановке).
        if (guestMatchPhone is null)  // SUBJECT-PHONE-GATE: gated — TD-03-ter, ARCHITECTURE_CYCLE16.md §245.4
            return BadRequest(
                "Это согласие записано на номер телефона, а не на учётную запись. Отозвать его отсюда " +
                "можно после подтверждения номера; если подтвердить номер невозможно, направьте отзыв " +
                "через форму обращения.");

        var subject = ConsentSubject.ForPhoneInCompany(guestMatchPhone, dto.CompanyId.Value);
        var revoked = await ledger.RevokeAsync(subject, dto.DocumentKey, purpose: null, dto.Reason);

        var photosDeleted = 0;
        var healthNotesDeleted = 0;
        if (dto.DocumentKey == LegalTextKey.PhotoConsent)
        {
            var photos = await db.ClientNotePhotos.Include(p => p.ClientNote)
                .Where(p => p.CompanyId == dto.CompanyId
                            && (p.ClientNote.ClientId == userId || (guestMatchPhone != null && p.ClientNote.GuestPhone == guestMatchPhone)))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                .ToListAsync();
            photosDeleted = photos.Count;
            if (photos.Count > 0)
            {
                var paths = photos.Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();
                db.ClientNotePhotos.RemoveRange(photos);
                await db.SaveChangesAsync();
                foreach (var (full, thumb) in paths)
                {
                    storage.DeletePrivate(full);
                    storage.DeletePrivate(thumb);
                }
            }
        }
        else // HealthDataConsent
        {
            var healthNotes = await db.ClientHealthNotes
                .Where(n => n.CompanyId == dto.CompanyId && (n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone)))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                .ToListAsync();
            healthNotesDeleted = healthNotes.Count;
            if (healthNotes.Count > 0)
            {
                db.ClientHealthNotes.RemoveRange(healthNotes);
                await db.SaveChangesAsync();
            }
        }

        var effects = new RevokeEffectsDto(photosDeleted, healthNotesDeleted, [], 0);
        return Ok(new RevokeConsentResponseDto(revoked, effects));
    }

    [HttpGet("consents/revoke-preview")]
    public async Task<ActionResult<RevokeEffectsDto>> RevokeConsentPreview([FromQuery] string? purpose)
    {
        ConsentPurpose? parsedPurpose = null;
        if (purpose is not null)
        {
            if (!Enum.TryParse<ConsentPurpose>(purpose, ignoreCase: true, out var parsed))
                return BadRequest($"Неизвестная цель '{purpose}'.");
            parsedPurpose = parsed;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var effects = await ApplyOrPreviewRevokeEffectsAsync(userId, parsedPurpose, apply: false);
        return Ok(effects);
    }

    /// <summary>
    /// The cascade table from ARCHITECTURE_CYCLE5.md §47.1, computed once for both the real revoke and
    /// its preview. `purpose: null` means "the whole PdnConsent document" — every cascade below applies,
    /// plus the optional profile fields (§47.1's fourth row). A specific purpose applies only its own row.
    /// </summary>
    private async Task<RevokeEffectsDto> ApplyOrPreviewRevokeEffectsAsync(string userId, ConsentPurpose? purpose, bool apply)
    {
        var wholeDocument = purpose is null;
        var photosDeleted = 0;
        var healthNotesDeleted = 0;
        var queuedNotificationsCancelled = 0;
        var profileFieldsCleared = new List<string>();
        // TD-03 (ARCHITECTURE_CYCLE16.md §245.2 row 4 — a place the spec itself did not name). Before
        // this gate an unverified account could both DESTROY a stranger's health notes (apply: true)
        // AND, via the preview endpoint, learn their COUNT without deleting anything — a double
        // violation of §272/A2 ("not an oracle"). Only the health-notes branch below reads a phone at
        // all; the WorkPhotos branch above is ClientId-only by construction and needs no gate.
        var accountForScope = await userManager.FindByIdAsync(userId);
        var scope = accountForScope is null
            ? new SubjectScope(userId, null, null)
            : await subjectScopeResolver.ForAccountAsync(accountForScope, HttpContext.RequestAborted);
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation(
                "guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId,
                apply ? "profile/consents/revoke" : "profile/consents/revoke-preview");
        }

        // Code review, "заодно": the four sections below used to run as four independent SaveChangesAsync
        // calls with no shared transaction — a crash between any two of them left a partially-applied
        // revoke (e.g. photos gone but the pending-notification cancellation never happened). One
        // transaction for the whole apply pass, same pattern DeleteAccount already uses to mix
        // db.SaveChangesAsync and userManager calls (both go through this same AppDbContext/connection).
        // Preview (apply == false) writes nothing at all, so it opens no transaction.
        await using var transaction = apply ? await db.Database.BeginTransactionAsync() : null;
        // Files (photo pair + avatar) are collected here and only actually deleted AFTER the transaction
        // below commits (ARCHITECTURE.md §1.4: "row goes first") — now that all four sections share one
        // transaction, deleting a file mid-pass (as an earlier version did, right after that section's
        // own SaveChanges) would leave an orphaned-on-disk file with no row if a LATER section made the
        // whole transaction roll back.
        var photoPathsToDelete = new List<(string Full, string Thumb)>();
        string? avatarUrlToDelete = null;

        if (wholeDocument || purpose == ConsentPurpose.WorkPhotos)
        {
            // "About the user" — every photo on a note filed against THEIR account, in any company
            // (§47.1's "во всех компаниях"), never a guest-path note (that has no ClientId to match).
            var photos = await db.ClientNotePhotos.Include(p => p.ClientNote)
                .Where(p => p.ClientNote.ClientId == userId).ToListAsync();
            photosDeleted = photos.Count;
            if (apply && photos.Count > 0)
            {
                photoPathsToDelete.AddRange(photos.Select(p => (p.StoragePath, p.ThumbnailPath)));
                db.ClientNotePhotos.RemoveRange(photos);
                await db.SaveChangesAsync();
            }
        }

        if (wholeDocument || purpose == ConsentPurpose.HealthData)
        {
            var healthNotes = await db.ClientHealthNotes
                .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                .ToListAsync();
            healthNotesDeleted = healthNotes.Count;
            if (apply && healthNotes.Count > 0)
            {
                db.ClientHealthNotes.RemoveRange(healthNotes);
                await db.SaveChangesAsync();
            }
        }

        if (wholeDocument || purpose == ConsentPurpose.ProviderDelivery)
        {
            // §47.1: not skipped silently at send time — CANCELLED now, with a reason, so the delivery
            // log shows why (§55.1 R11's "новая ветка гейта" is the mirror of this: NEW rows stop being
            // queued at all via NotificationGate/T-24, ARCHITECTURE_CYCLE5.md §52.3).
            var pending = await db.OutboundNotifications
                .Where(n => n.RecipientUserId == userId && n.Status == NotificationStatus.Pending).ToListAsync();
            queuedNotificationsCancelled = pending.Count;
            if (apply && pending.Count > 0)
            {
                foreach (var row in pending)
                {
                    row.Status = NotificationStatus.Cancelled;
                    row.Reason = NotificationReason.NoProviderDeliveryConsent;
                }
                await db.SaveChangesAsync();
            }
        }

        if (wholeDocument)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                if (!string.IsNullOrEmpty(user.Email)) profileFieldsCleared.Add("email");
                if (!string.IsNullOrEmpty(user.AvatarUrl)) profileFieldsCleared.Add("avatarUrl");
                if (apply && profileFieldsCleared.Count > 0)
                {
                    avatarUrlToDelete = user.AvatarUrl;
                    user.Email = null;
                    user.AvatarUrl = null;
                    await userManager.UpdateAsync(user);
                }
            }
        }

        if (transaction is not null) await transaction.CommitAsync();

        // Only after the commit succeeds (ARCHITECTURE.md §1.4) — see the comment above the transaction.
        foreach (var (full, thumb) in photoPathsToDelete)
        {
            storage.DeletePrivate(full);
            storage.DeletePrivate(thumb);
        }
        if (avatarUrlToDelete is not null) storage.DeletePublic(avatarUrlToDelete);

        return new RevokeEffectsDto(photosDeleted, healthNotesDeleted, profileFieldsCleared, queuedNotificationsCancelled);
    }

    private async Task<ConsentsDto> BuildConsentsDtoAsync(string userId, LegalDocument pdnDoc)
    {
        var subject = ConsentSubject.ForUser(userId);
        // knownPhone (code review В3): "Мои согласия" must also show salon-recorded PhotoConsent/
        // HealthDataConsent rows — see ConsentLedger.HistoryAsync's doc comment.
        var knownPhone = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        var history = await ledger.HistoryAsync(subject, knownPhone);

        // "granted" is the latest row per purpose UNDER PdnConsent specifically, revoked or not — a
        // revoked purpose still needs to show up (with revokedAt set) so the profile screen can render
        // it as "revoked" rather than making it look like it was never granted (API_CONTRACT_CYCLE5.md
        // §41.1). Deliberately NOT a "current grants" read (ConsentLedger.CurrentAsync), which only
        // ever returns non-revoked rows — that answers a different question ("what currently applies"),
        // used for gating decisions elsewhere, not for this audit-style view.
        var granted = history
            .Where(s => s.DocumentKey == LegalDocumentType.PdnConsent.ToString() && s.Purpose is not null)
            .GroupBy(s => s.Purpose)
            .Select(g => g.First()) // history is already newest-first
            .OrderBy(s => s.GrantedAtUtc)
            .Select(s => new ConsentGrantedDto(s.Purpose!.Value.ToString(), s.DocumentVersion, s.GrantedAtUtc, s.RevokedAtUtc))
            .ToList();

        var versionOutdated = granted.Any(g => g.RevokedAt is null && g.Version != pdnDoc.Version);

        var documentDto = new ConsentDocumentDto(
            pdnDoc.Type.ToString(), pdnDoc.Version, pdnDoc.IsDraft,
            pdnDoc.Purposes.Select(p => new LegalPurposeDto(p.Key.ToString(), p.Title)).ToList());

        return new ConsentsDto(documentDto, granted, versionOutdated, history.Select(SubjectDataExporter.ToExportConsentDto).ToList());
    }
}


// API_CONTRACT_CYCLE5.md §41 — GET/POST /api/profile/consents. LegalPurposeDto is the same record
// LegalController's GET /api/legal/documents uses (same namespace) — one shape for "what a purpose is",
// so the frontend never has to reconcile two slightly different purpose objects from two endpoints.
public record ConsentDocumentDto(string Type, string Version, bool IsDraft, List<LegalPurposeDto> Purposes);
public record ConsentGrantedDto(string Purpose, string Version, DateTime GrantedAt, DateTime? RevokedAt);
public record ConsentsDto(ConsentDocumentDto Document, List<ConsentGrantedDto> Granted, bool VersionOutdated, List<ExportConsentDto> History);
public record SubmitConsentDto(string DocumentKey, string Version, List<string>? Purposes);
// CompanyId appended (code review В3): required only for the salon-scoped DocumentKeys
// (LegalTextKey.PhotoConsent/HealthDataConsent) — default null keeps every existing PdnConsent caller
// compiling and behaving exactly as before.
public record RevokeConsentDto(string DocumentKey, string? Purpose, string? Reason, Guid? CompanyId = null);
public record RevokeEffectsDto(int PhotosDeleted, int HealthNotesDeleted, List<string> ProfileFieldsCleared, int QueuedNotificationsCancelled);
public record RevokeConsentResponseDto(int Revoked, RevokeEffectsDto Effects);
