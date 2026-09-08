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
    LegalDocumentProvider provider, AppDbContext db, UserManager<AppUser> userManager, TokenService tokenService)
    : ControllerBase
{
    private const string UnavailableMessage = "Правовые документы временно недоступны.";

    // Public. Metadata for both documents — enough for the footer, the registration form and version
    // comparison, without shipping the (potentially large) HTML text (ARCHITECTURE.md §1, API_CONTRACT §1).
    [HttpGet("documents")]
    public ActionResult<LegalDocumentListDto> GetDocuments()
    {
        var snapshot = provider.Current;
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        return Ok(new LegalDocumentListDto(snapshot.Documents.Values
            .OrderBy(d => d.Type)
            .Select(MapToMetaDto)
            .ToList()));
    }

    // Public. Metadata AND text for one document.
    [HttpGet("documents/{type}")]
    public ActionResult<LegalDocumentDto> GetDocument(string type)
    {
        if (!Enum.TryParse<LegalDocumentType>(type, ignoreCase: true, out var documentType))
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
            doc.ChangeKind.ToString(), doc.ContentHtml));
    }

    // Requires auth, but IS in LegalConsentFilter's allow-list — a caller blocked by 451 elsewhere must
    // still be able to ask "what exactly is required of me" (API_CONTRACT.md §0.4, §3). Computed purely
    // from the token's claims and the in-memory snapshot: no database query (ARCHITECTURE.md §6.3,
    // SPEC §7 p.1).
    [HttpGet("consent-status")]
    [Authorize]
    public ActionResult<ConsentStatusDto> GetConsentStatus()
    {
        var snapshot = provider.Current;
        var documents = new List<ConsentStatusDocumentDto>();
        var requiresAcceptance = false;
        var hasEditorialMismatch = false;

        if (snapshot is not null)
        {
            foreach (var doc in snapshot.Documents.Values.OrderBy(d => d.Type))
            {
                var acceptedVersion = User.FindFirst(LegalConsentFilter.ClaimNameFor(doc.Type))?.Value;
                documents.Add(new ConsentStatusDocumentDto(doc.Type.ToString(), doc.Version, acceptedVersion, doc.ChangeKind.ToString()));

                if (acceptedVersion == doc.Version) continue;
                if (doc.ChangeKind == LegalChangeKind.Material) requiresAcceptance = true;
                else hasEditorialMismatch = true;
            }
        }

        // Material always wins over Editorial (ARCHITECTURE.md §6.3 table) — the two flags are never
        // both true in the response.
        var showBanner = !requiresAcceptance && hasEditorialMismatch;

        return Ok(new ConsentStatusDto(requiresAcceptance, showBanner, documents));
    }

    // Requires auth, in the allow-list — otherwise a caller blocked by 451 could never reach the one
    // endpoint that lifts the block. Body versions are compared against the CURRENT snapshot, not
    // merely recorded: closes the race where the operator replaces the text again while the user is
    // mid-read (API_CONTRACT.md §4).
    [HttpPost("accept")]
    [Authorize]
    public async Task<ActionResult<AcceptLegalResponseDto>> Accept([FromBody] AcceptLegalDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PrivacyVersion) || string.IsNullOrWhiteSpace(dto.TermsVersion))
            return BadRequest("Обе версии документов обязательны.");

        var snapshot = provider.Current;
        var privacyDoc = snapshot?.Get(LegalDocumentType.Privacy);
        var termsDoc = snapshot?.Get(LegalDocumentType.Terms);
        if (privacyDoc is null || termsDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        if (dto.PrivacyVersion != privacyDoc.Version || dto.TermsVersion != termsDoc.Version)
            return Conflict("Документы были обновлены ещё раз — перечитайте и примите новую редакцию.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        var acceptedAt = DateTime.UtcNow;
        await UpsertConsentAsync(user.Id, LegalDocumentType.Privacy, privacyDoc.Version, acceptedAt);
        await UpsertConsentAsync(user.Id, LegalDocumentType.Terms, termsDoc.Version, acceptedAt);
        await db.SaveChangesAsync();

        // A new token is mandatory here, not an optimization (ARCHITECTURE.md §6.3 p.5): the claims are
        // baked in at issuance, so without a fresh one the very next request would still carry the OLD
        // version and get 451 again.
        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.GenerateToken(user, roles, privacyDoc.Version, termsDoc.Version);

        return Ok(new AcceptLegalResponseDto(token, acceptedAt));
    }

    private async Task UpsertConsentAsync(string userId, LegalDocumentType type, string version, DateTime acceptedAtUtc)
    {
        var existing = await db.UserConsents.FirstOrDefaultAsync(c => c.UserId == userId && c.DocumentType == type);
        if (existing is null)
        {
            db.UserConsents.Add(new UserConsent
            {
                Id = Guid.NewGuid(), UserId = userId, DocumentType = type, Version = version, AcceptedAtUtc = acceptedAtUtc
            });
        }
        else
        {
            existing.Version = version;
            existing.AcceptedAtUtc = acceptedAtUtc;
        }
    }

    private static LegalDocumentMetaDto MapToMetaDto(LegalDocument d) =>
        new(d.Type.ToString(), d.Title, d.Version, d.EffectiveFrom, d.IsDraft, d.ChangeKind.ToString());
}

public record LegalDocumentMetaDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind);

public record LegalDocumentListDto(List<LegalDocumentMetaDto> Documents);

public record LegalDocumentDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind,
    string ContentHtml);

public record ConsentStatusDocumentDto(string Type, string Version, string? AcceptedVersion, string ChangeKind);

public record ConsentStatusDto(bool RequiresAcceptance, bool ShowBanner, List<ConsentStatusDocumentDto> Documents);

public record AcceptLegalDto(string? PrivacyVersion, string? TermsVersion);

public record AcceptLegalResponseDto(string Token, DateTime AcceptedAt);
