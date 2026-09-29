using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/legal")]
public class LegalController(
    LegalDocumentProvider provider, ConsentLedger ledger, UserManager<AppUser> userManager, TokenService tokenService, AppDbContext db)
    : ControllerBase
{
    private const string UnavailableMessage = "Правовые документы временно недоступны.";

    // Public. Metadata for all five documents and all six interface texts — enough for the footer, the
    // registration form and version comparison, without shipping the (potentially large) HTML text
    // (API_CONTRACT_CYCLE5.md §39.1). `purposes` is the source of truth for the PdnConsent form — the
    // frontend builds it from here, never from a hardcoded array (§39.1's "если юрист изменит набор
    // целей, форма подстроится без релиза фронта").
    [HttpGet("documents")]
    public ActionResult<LegalManifestDto> GetDocuments()
    {
        var snapshot = provider.Current;
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        var documents = snapshot.Documents.Values.OrderBy(d => d.Type).Select(MapToMetaDto).ToList();
        var uiTexts = snapshot.UiTexts.Values.OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new LegalUiTextMetaDto(t.Key, t.Version, t.IsDraft)).ToList();

        return Ok(new LegalManifestDto(documents, uiTexts));
    }

    // Public. Metadata AND text for one document. {type} now accepts five values; unknown → 404 (not 500).
    [HttpGet("documents/{type}")]
    public ActionResult<LegalDocumentDto> GetDocument(string type)
    {
        // Code review (contract check, cycle 11, round 2): the contract's {type} is a closed enum of
        // NAMED string values ("Privacy", "TermsClient", ...) — a bare integer is never one of them, in
        // or out of range. Enum.TryParse<T> happily parses ANY integer string ("0", "2", "99", "-1") by
        // binding it to the enum's underlying numeric value regardless of whether the caller wrote a
        // name; Enum.IsDefined alone only catches the OUT-OF-RANGE case (e.g. "99"), not an in-range
        // numeric alias like "0" resolving to Privacy. Rejecting any input that parses as a plain
        // integer closes both holes: only a case-insensitive match on one of the declared member names
        // is accepted, exactly as documented in the parameter's description.
        if (int.TryParse(type, out _)
            || !Enum.TryParse<LegalDocumentType>(type, ignoreCase: true, out var documentType)
            || !Enum.IsDefined(documentType))
            return NotFound();

        var snapshot = provider.Current;
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        var doc = snapshot.Get(documentType);
        if (doc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        // ARCHITECTURE.md §2: text changes two or three times over the product's lifetime, and this page
        // is public — five minutes of caching doesn't get in the way of the operational replacement,
        // which is already picked up by the provider within ReloadSeconds.
        Response.Headers.CacheControl = "public, max-age=300";

        return Ok(new LegalDocumentDto(
            doc.Type.ToString(), doc.Title, doc.Version, doc.EffectiveFrom, doc.IsDraft,
            doc.ChangeKind.ToString(), doc.Gate.ToString(), LegalRoutes.UrlFor(doc.Type),
            doc.Purposes.Count == 0 ? null : doc.Purposes.Select(p => new LegalPurposeDto(p.Key.ToString(), p.Title)).ToList(),
            doc.ContentHtml));
    }

    // Public, NEW (API_CONTRACT_CYCLE5.md §39.3). An interface text (D5, D7, D8, D10–D12) — versioned
    // like a document, but never gates access (§43.1): there is deliberately no claim, no 451 branch,
    // nothing in consent-status for these keys.
    [HttpGet("texts/{key}")]
    public ActionResult<LegalUiTextDto> GetText(string key)
    {
        var snapshot = provider.Current;
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        var text = snapshot.GetText(key);
        if (text is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(new LegalUiTextDto(text.Key, text.Version, text.IsDraft, text.ContentHtml));
    }

    // Requires auth, but IS in LegalConsentFilter's allow-list — a caller blocked by 451 elsewhere must
    // still be able to ask "what exactly is required of me" (API_CONTRACT.md §0.4, §3). Computed purely
    // from the token's claims and the in-memory snapshot: no database query (ARCHITECTURE.md §6.3,
    // SPEC §7 p.1). Only documents with `gate != None` are reported — PdnConsent/ChannelRiskNotice never
    // block anything, and don't belong in a "what's blocking me" answer (API_CONTRACT_CYCLE5.md §39.4).
    [HttpGet("consent-status")]
    [Authorize]
    public ActionResult<ConsentStatusDto> GetConsentStatus()
    {
        var snapshot = provider.Current;
        var documents = new List<ConsentStatusDocumentDto>();
        var requiresAcceptance = false;
        var ownerActionBlocked = false;
        var hasEditorialMismatch = false;

        if (snapshot is not null)
        {
            foreach (var doc in snapshot.Documents.Values.Where(d => d.Gate != LegalGate.None).OrderBy(d => d.Type))
            {
                var claimName = LegalConsentFilter.ClaimNameFor(doc.Type);
                var acceptedVersion = claimName is null ? null : User.FindFirst(claimName)?.Value;
                documents.Add(new ConsentStatusDocumentDto(doc.Type.ToString(), doc.Version, acceptedVersion, doc.ChangeKind.ToString(), doc.Gate.ToString()));

                if (acceptedVersion == doc.Version) continue;

                // Code review В1: a MISSING claim (acceptedVersion is null — this account has simply
                // never touched TermsOwner, e.g. an ordinary client who never created a company) is not
                // the same fact as a STALE claim (a real version string that no longer matches). Every
                // account has Privacy/TermsClient claims from registration, so Global's null case never
                // legitimately happens — but OwnerScope's null case is the common case, and until now
                // this loop treated it exactly like a stale claim, disagreeing with
                // RequiresOwnerTermsFilter's own `if (acceptedVersion is null) return;` (missing claim =
                // not yet applicable, not a block). Two places reading the same claim must agree.
                if (acceptedVersion is null && doc.Gate == LegalGate.OwnerScope) continue;

                if (doc.ChangeKind != LegalChangeKind.Material)
                {
                    hasEditorialMismatch = true;
                    continue;
                }

                if (doc.Gate == LegalGate.Global) requiresAcceptance = true;
                else if (doc.Gate == LegalGate.OwnerScope) ownerActionBlocked = true;
            }
        }

        // Material always wins over Editorial (ARCHITECTURE.md §6.3 table) — the two flags are never
        // both true in the response for the same document, but a Global mismatch on one document and an
        // Editorial mismatch on another can coexist; showBanner only reflects documents that aren't
        // ALSO the reason for a hard block.
        var showBanner = !requiresAcceptance && hasEditorialMismatch;

        return Ok(new ConsentStatusDto(requiresAcceptance, ownerActionBlocked, showBanner, documents));
    }

    // Requires auth, in the allow-list — otherwise a caller blocked by 451 could never reach the one
    // endpoint that lifts the block. Body versions are compared against the CURRENT snapshot, not
    // merely recorded: closes the race where the operator replaces the text again while the user is
    // mid-read (API_CONTRACT.md §4). Accepts any subset of {Privacy, TermsClient, TermsOwner} in one
    // call — a list, not two fixed fields, so a sixth blocking document would be an addition, not a
    // breaking change (API_CONTRACT_CYCLE5.md §39.5).
    [HttpPost("accept")]
    [Authorize]
    public async Task<ActionResult<AcceptLegalResponseDto>> Accept([FromBody] AcceptLegalRequestDto dto)
    {
        if (dto.Accept is not { Count: > 0 })
            return BadRequest("Список принимаемых документов не может быть пустым.");

        var snapshot = provider.Current;
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();
        var subject = ConsentSubject.ForUser(user.Id);

        var toAccept = new List<(LegalDocumentType Type, LegalDocument Doc)>();
        foreach (var item in dto.Accept)
        {
            if (!Enum.TryParse<LegalDocumentType>(item.Type, ignoreCase: true, out var type))
                return BadRequest($"Неизвестный тип документа '{item.Type}'.");

            var doc = snapshot.Get(type);
            if (doc is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

            // Only gate-bearing documents are accepted through this endpoint — PdnConsent is a separate,
            // purpose-scoped call (POST /api/profile/consents, §41.2); accepting it here would blur the
            // "recorded separately from other documents" guarantee Art. 9 needs (§46.2).
            if (doc.Gate == LegalGate.None)
                return BadRequest($"Документ '{item.Type}' принимается не здесь.");

            if (item.Version != doc.Version)
                return Conflict("Документы были обновлены ещё раз — перечитайте и примите новую редакцию.");

            toAccept.Add((type, doc));
        }

        var acceptedAt = DateTime.UtcNow;
        foreach (var (type, doc) in toAccept)
        {
            var act = type == LegalDocumentType.Privacy ? ConsentAct.Acknowledged : ConsentAct.Accepted;
            await ledger.GrantAsync(new ConsentGrant(
                subject, doc.Type.ToString(), doc.Version, doc.ContentHash, Purpose: null, act, ConsentSource.ReAcceptance,
                IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent: Request.Headers.UserAgent.ToString()));
        }

        // A new token is mandatory here, not an optimization (ARCHITECTURE.md §6.3 p.5): the claims are
        // baked in at issuance, so without a fresh one the very next request would still carry the OLD
        // version and get 451 again. Every claim-bearing document is re-resolved (not just the ones in
        // THIS call) so the new token is complete regardless of which subset was just accepted.
        var roles = await userManager.GetRolesAsync(user);
        var privacyVersion = await CurrentClaimVersionAsync(subject, snapshot, LegalDocumentType.Privacy);
        var termsVersion = await CurrentClaimVersionAsync(subject, snapshot, LegalDocumentType.TermsClient);
        var ownerTermsVersion = await CurrentClaimVersionAsync(subject, snapshot, LegalDocumentType.TermsOwner);
        var token = tokenService.GenerateToken(user, roles, privacyVersion, termsVersion, ownerTermsVersion);

        return Ok(new AcceptLegalResponseDto(token, acceptedAt));
    }

    /// <summary>Current non-revoked grant version for one claim-bearing document type, or null if the
    /// subject never accepted it (a non-owner has no TermsOwner grant, for instance).</summary>
    private async Task<string?> CurrentClaimVersionAsync(ConsentSubject subject, LegalSnapshot snapshot, LegalDocumentType type)
    {
        var doc = snapshot.Get(type);
        if (doc is null) return null;
        var state = await ledger.CurrentAsync(subject, type.ToString(), purpose: null);
        return state?.DocumentVersion;
    }

    // ARCHITECTURE_CYCLE20.md §404.1/§404.2, API_CONTRACT_CYCLE20.md §434.1 (US-20-03, NEW) — `/api/legal/`
    // is already in LegalConsentFilter's allow-list by PREFIX (§430), so a suspended owner or a caller
    // with a pending Material revision can still read/acknowledge notices without any extra wiring here.
    // "3 запроса к БД на один HTTP-запрос" (§404.2): caller's held account, its plan, all currently
    // visible notices, and the caller's own acknowledgements for them.
    [HttpGet("notices")]
    [Authorize]
    public async Task<ActionResult<PlatformNoticeListDto>> GetNotices([FromQuery] string? scope)
    {
        scope ??= "pending";
        if (scope is not ("pending" or "all"))
            return BadRequest($"Неизвестное значение scope '{scope}'. Ожидается pending или all.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var nowUtc = DateTime.UtcNow;
        var visible = await db.PlatformNotices.AsNoTracking().Where(n => n.VisibleUntilUtc > nowUtc).ToListAsync();

        // The system-free plan is only ever needed to resolve an OwnersOnPlans match for an account with
        // no subscription row — looked up lazily so an account/kind mix that never needs it costs nothing.
        var facts = await LoadCallerFactsAsync(userId, User.IsInRole("SuperAdmin"),
            needsFreePlan: visible.Any(n => n.AudienceType == NoticeAudienceType.OwnersOnPlans));

        var matched = visible.Where(n => NoticeAudience.Matches(n, facts)).ToList();

        var matchedIds = matched.Select(n => n.Id).ToList();
        var acknowledgedAt = matchedIds.Count == 0
            ? new Dictionary<Guid, DateTime>()
            : await db.PlatformNoticeAcknowledgements.AsNoTracking()
                .Where(a => a.UserId == userId && matchedIds.Contains(a.NoticeId))
                .ToDictionaryAsync(a => a.NoticeId, a => a.AcknowledgedAtUtc);

        if (scope == "pending")
            matched = matched.Where(n => n.RevokedAtUtc is null && !acknowledgedAt.ContainsKey(n.Id)).ToList();

        var items = matched.OrderByDescending(n => n.PublishedAtUtc).Select(n => MapToNoticeDto(n, acknowledgedAt)).ToList();

        return Ok(new PlatformNoticeListDto(PlatformNoticeTexts.AcknowledgeButtonText, PlatformNoticeTexts.AcknowledgeCaption, items));
    }

    // API_CONTRACT_CYCLE20.md §434.2 (US-20-03, NEW). Idempotent by the (NoticeId, UserId) unique index —
    // a repeat click answers 200 without moving AcknowledgedAtUtc or writing a second row.
    [HttpPost("notices/{id:guid}/acknowledge")]
    [Authorize]
    public async Task<IActionResult> AcknowledgeNotice(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notice = await db.PlatformNotices.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);
        var nowUtc = DateTime.UtcNow;

        // "Уведомления нет, срок видимости вышел или вызывающий не адресат (не различаются)" — the three
        // cases share one 404 so a caller can never probe for a notice they aren't the addressee of.
        if (notice is null || notice.VisibleUntilUtc <= nowUtc) return NotFound();

        var facts = await LoadCallerFactsAsync(userId, User.IsInRole("SuperAdmin"),
            needsFreePlan: notice.AudienceType == NoticeAudienceType.OwnersOnPlans);
        if (!NoticeAudience.Matches(notice, facts)) return NotFound();

        if (notice.RevokedAtUtc is not null) return Conflict("Уведомление отозвано.");

        var existing = await db.PlatformNoticeAcknowledgements.FirstOrDefaultAsync(a => a.NoticeId == id && a.UserId == userId);
        if (existing is null)
        {
            db.PlatformNoticeAcknowledgements.Add(new PlatformNoticeAcknowledgement
            {
                Id = Guid.NewGuid(),
                NoticeId = id,
                UserId = userId,
                BillingAccountId = PlatformNoticeRules.IsOwnerFacingAudience(notice.AudienceType) ? facts.HeldBillingAccountId : null,
                AcknowledgedAtUtc = nowUtc,
            });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // A genuinely concurrent double-click lost the unique-index race — treat it exactly like
                // the "already acknowledged" branch above (idempotent per §434.2), not as an error. Not a
                // `when` filter: awaiting inside a catch filter expression is illegal (CS7094).
                if (!await db.PlatformNoticeAcknowledgements.AnyAsync(a => a.NoticeId == id && a.UserId == userId))
                    throw;
            }
        }

        var ackMap = await db.PlatformNoticeAcknowledgements.AsNoTracking()
            .Where(a => a.NoticeId == id && a.UserId == userId)
            .ToDictionaryAsync(a => a.NoticeId, a => a.AcknowledgedAtUtc);

        return Ok(MapToNoticeDto(notice, ackMap));
    }

    // API_CONTRACT_CYCLE20.md §434.3 (US-20-03, NEW) — the TermsChange future-edition snapshot
    // (ARCHITECTURE_CYCLE20.md §404.4). Sandboxed CSP so even a compromised SuperAdmin session can't
    // smuggle a script into a document this endpoint serves.
    [HttpGet("notices/{id:guid}/attachment")]
    [Authorize]
    public async Task<IActionResult> GetNoticeAttachment(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notice = await db.PlatformNotices.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);
        var nowUtc = DateTime.UtcNow;
        if (notice is null || notice.VisibleUntilUtc <= nowUtc || notice.AttachmentHtml is null) return NotFound();

        var facts = await LoadCallerFactsAsync(userId, User.IsInRole("SuperAdmin"),
            needsFreePlan: notice.AudienceType == NoticeAudienceType.OwnersOnPlans);
        if (!NoticeAudience.Matches(notice, facts)) return NotFound();

        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; style-src 'unsafe-inline'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(notice.AttachmentHtml, "text/html; charset=utf-8");
    }

    /// <summary>§404.2's "CallerFacts — держатель каких аккаунтов, на каких тарифах, суперадмин ли". The
    /// single place all three notice endpoints (list, acknowledge, attachment) resolve caller facts from —
    /// previously duplicated three times, which let <c>GetNoticeAttachment</c> drift out of sync (code
    /// review finding, cycle 20: an owner with no <see cref="Core.Entities.AccountSubscription"/> row —
    /// i.e. on the implicit system-free plan — matched an <c>OwnersOnPlans</c> notice that lists Free in
    /// its <c>GetNotices</c>/<c>AcknowledgeNotice</c>, but got 404 on the attachment because this lookup was
    /// skipped there). <paramref name="needsFreePlan"/> resolves
    /// <see cref="NoticeCallerFacts.SystemFreePlanConfigId"/> only when the caller actually might need it
    /// (an <c>OwnersOnPlans</c> notice is in play) — the lookup is otherwise skipped to keep the cheap path
    /// cheap.</summary>
    private async Task<NoticeCallerFacts> LoadCallerFactsAsync(string userId, bool isSuperAdmin, bool needsFreePlan)
    {
        var heldAccountId = await db.BillingAccounts.AsNoTracking()
            .Where(a => a.OwnerUserId == userId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync();

        Guid? heldPlanConfigId = null;
        if (heldAccountId is { } accountId)
        {
            heldPlanConfigId = await db.AccountSubscriptions.AsNoTracking()
                .Where(s => s.BillingAccountId == accountId).Select(s => s.PlanConfigId).FirstOrDefaultAsync();
        }

        Guid? systemFreePlanConfigId = null;
        if (needsFreePlan && heldAccountId is not null && heldPlanConfigId is null)
        {
            systemFreePlanConfigId = await db.SubscriptionPlanConfigs.AsNoTracking()
                .Where(p => p.IsSystemFree && p.Line == CompanyKind.Services).Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
        }

        return new NoticeCallerFacts(isSuperAdmin, heldAccountId, heldPlanConfigId, systemFreePlanConfigId);
    }

    private static PlatformNoticeDto MapToNoticeDto(PlatformNotice n, IReadOnlyDictionary<Guid, DateTime> acknowledgedAt)
    {
        var acknowledged = acknowledgedAt.TryGetValue(n.Id, out var ackAt);
        return new PlatformNoticeDto(
            n.Id, n.Kind.ToString(), n.Title, n.Body, n.LinkUrl, n.EffectiveFrom, n.PublishedAtUtc, n.VisibleUntilUtc,
            n.AttachmentTitle is not null ? new NoticeAttachmentRefDto(n.AttachmentTitle, n.AttachmentSha256!) : null,
            acknowledged, acknowledged ? ackAt : null, n.RevokedAtUtc);
    }

    private static LegalDocumentMetaDto MapToMetaDto(LegalDocument d) =>
        new(d.Type.ToString(), d.Title, d.Version, d.EffectiveFrom, d.IsDraft, d.ChangeKind.ToString(), d.Gate.ToString(),
            LegalRoutes.UrlFor(d.Type),
            d.Purposes.Count == 0 ? null : d.Purposes.Select(p => new LegalPurposeDto(p.Key.ToString(), p.Title)).ToList());
}

public record LegalPurposeDto(string Key, string Title);

public record LegalDocumentMetaDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind, string Gate,
    string Url, List<LegalPurposeDto>? Purposes);

public record LegalUiTextMetaDto(string Key, string Version, bool IsDraft);

public record LegalManifestDto(List<LegalDocumentMetaDto> Documents, List<LegalUiTextMetaDto> UiTexts);

// CYCLE5-BREAKING: gate/url/purposes added, matching LegalDocumentMetaDto's shape (API_CONTRACT_CYCLE5.md
// §39.2 "без изменений по форме" relative to §39.1 — code-reviewer/frontend feedback: the two responses
// had drifted, this closes the gap by extending the single-document shape rather than narrowing the list).
public record LegalDocumentDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind,
    string Gate, string Url, List<LegalPurposeDto>? Purposes, string ContentHtml);

public record LegalUiTextDto(string Key, string Version, bool IsDraft, string ContentHtml);

// CYCLE5-BREAKING: field renamed Version → CurrentVersion (API_CONTRACT_CYCLE5.md §39.4's
// `currentVersion`/`acceptedVersion` pair) — frontend feedback during integration: a lone `version` next
// to `acceptedVersion` doesn't read as "current vs. accepted" unambiguously, `currentVersion` does.
public record ConsentStatusDocumentDto(string Type, string CurrentVersion, string? AcceptedVersion, string ChangeKind, string Gate);

public record ConsentStatusDto(bool RequiresAcceptance, bool OwnerActionBlocked, bool ShowBanner, List<ConsentStatusDocumentDto> Documents);

public record AcceptLegalItemDto(string Type, string Version);
public record AcceptLegalRequestDto(List<AcceptLegalItemDto> Accept);
public record AcceptLegalResponseDto(string Token, DateTime AcceptedAt);

// ARCHITECTURE_CYCLE20.md §404.1/§404.2, API_CONTRACT_CYCLE20.md §434.1 (US-20-03) — the addressee-facing
// notice shape. Field names/casing match frontend/src/types/api-cycle20.generated.ts's PlatformNoticeDto
// exactly (frontend-developer generated it from this cycle's contract ahead of this implementation).
public record NoticeAttachmentRefDto(string Title, string Sha256);

public record PlatformNoticeDto(
    Guid Id, string Kind, string Title, string Body, string? LinkUrl, DateOnly? EffectiveFrom,
    DateTime PublishedAt, DateTime VisibleUntil, NoticeAttachmentRefDto? Attachment,
    bool Acknowledged, DateTime? AcknowledgedAt, DateTime? RevokedAt);

public record PlatformNoticeListDto(string AcknowledgeButtonText, string AcknowledgeCaption, List<PlatformNoticeDto> Items);
