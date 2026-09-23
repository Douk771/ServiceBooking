using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// GET /api/admin/legal/readiness — ARCHITECTURE_CYCLE11.md §116/§105.4. Read-only diagnostics for
/// SuperAdmin: is the currently-loaded legal snapshot draft-free, and if the operator published it right
/// now, how many people would be asked to re-accept something. Never mutates anything, never touches the
/// hot request path of any other endpoint.
///
/// NOTE for reviewers: ARCHITECTURE_CYCLE11.md §105.4 specifies that this endpoint and
/// `ServiceBooking.LegalKit status --json` share one report shape (`contracts/cycle11/legal-status.schema.json`).
/// That CLI project (T1/T3 of §109) is not part of this change — see the accompanying report. This
/// endpoint computes its own reduced version of the same report (documents/uiTexts/placeholders/ready/
/// blockers/impact/disclaimer) directly from the in-memory snapshot and the database, without depending
/// on a CLI project that doesn't exist yet in this tree. `links`/`drift`/`root`/`generatedAtUtc` fields
/// from the full schema are included where they can be computed without the CLI's file-system tooling;
/// `anchors` is populated from `LegalRoutes`, which the CLI is expected to reuse once it exists.
/// </summary>
[ApiController]
[Route("api/admin/legal")]
[Authorize(Roles = "SuperAdmin")]
public class AdminLegalController(LegalDocumentProvider legalDocuments, AppDbContext db) : ControllerBase
{
    private const string Disclaimer =
        "Проверено механически. Это НЕ юридическая вычитка: проверка ловит незаполненные плейсхолдеры, " +
        "битые ссылки и неполный комплект, но не ловит неверный по сути текст. Публикация требует обоих подтверждений.";

    [HttpGet("readiness")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LegalReadinessDto>> GetReadiness(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var snapshot = legalDocuments.Current;
        if (snapshot is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new LegalReadinessDto(
                DateTime.UtcNow, Ready: false,
                Blockers: [new LegalReadinessBlockerDto("LegalUnavailable", "Снимок правовых документов не загружен.")],
                Documents: [], UiTexts: [], Impact: [], Disclaimer));
        }

        var documentReports = snapshot.Documents.Values.OrderBy(d => d.Type)
            .Select(d => new LegalReadinessDocumentDto(
                d.Type.ToString(), d.Title, d.Version, d.EffectiveFrom, d.IsDraft, d.ChangeKind.ToString(),
                d.Gate.ToString(), LegalRoutes.UrlFor(d.Type), d.ContentHash, FindPlaceholders(d.ContentHtml)))
            .ToList();

        var uiTextReports = snapshot.UiTexts.Values.OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new LegalReadinessUiTextDto(t.Key, t.Version, t.IsDraft, t.ContentHash, FindPlaceholders(t.ContentHtml)))
            .ToList();

        var blockers = new List<LegalReadinessBlockerDto>();

        var draftDocuments = documentReports.Where(d => d.IsDraft).ToList();
        var draftUiTexts = uiTextReports.Where(t => t.IsDraft).ToList();
        if (draftDocuments.Count > 0 || draftUiTexts.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto(
                "DraftDocuments", $"{draftDocuments.Count} документов и {draftUiTexts.Count} текстов интерфейса"));

        var placeholderKinds = documentReports.SelectMany(d => d.Placeholders)
            .Concat(uiTextReports.SelectMany(t => t.Placeholders))
            .Select(p => p.Name).Distinct(StringComparer.Ordinal).Count();
        var placeholderOccurrences = documentReports.SelectMany(d => d.Placeholders)
            .Concat(uiTextReports.SelectMany(t => t.Placeholders)).Sum(p => p.Count);
        if (placeholderKinds > 0)
            blockers.Add(new LegalReadinessBlockerDto(
                "UnresolvedPlaceholders", $"{placeholderKinds} видов, {placeholderOccurrences} вхождений"));

        var impact = await GetImpactAsync(snapshot, ct);

        return Ok(new LegalReadinessDto(
            DateTime.UtcNow, Ready: blockers.Count == 0, blockers, documentReports, uiTextReports, impact, Disclaimer));
    }

    /// <summary>ARCHITECTURE_CYCLE11.md §116 `impact`: how many distinct subjects hold a stale accepted
    /// version of each gated document. Computed cold (one grouped query per gated document), never on
    /// the hot path — this endpoint is the only reader.</summary>
    private async Task<List<LegalReadinessImpactDto>> GetImpactAsync(LegalSnapshot snapshot, CancellationToken ct)
    {
        var results = new List<LegalReadinessImpactDto>();
        foreach (var doc in snapshot.Documents.Values.Where(d => d.Gate != LegalGate.None).OrderBy(d => d.Type))
        {
            var documentKey = doc.Type.ToString();

            // Latest ConsentRecord per subject for this document key; a subject whose latest recorded
            // version isn't the manifest's current version would be asked to re-accept.
            var staleCount = await db.ConsentRecords
                .Where(r => r.DocumentKey == documentKey && r.UserId != null)
                .GroupBy(r => r.UserId)
                .Select(g => g.OrderByDescending(r => r.GrantedAtUtc).First().DocumentVersion)
                .CountAsync(v => v != doc.Version, ct);

            if (staleCount > 0)
                results.Add(new LegalReadinessImpactDto(documentKey, doc.Gate.ToString(), staleCount));
        }
        return results;
    }

    private static List<LegalReadinessPlaceholderDto> FindPlaceholders(string html)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(html, LegalDocumentProvider.PlaceholderPattern);
        return matches.Select(m => m.Value.Trim('{', '}'))
            .GroupBy(name => name, StringComparer.Ordinal)
            .Select(g => new LegalReadinessPlaceholderDto(g.Key, g.Count()))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }
}

public record LegalReadinessPlaceholderDto(string Name, int Count);

public record LegalReadinessDocumentDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind,
    string Gate, string Url, string ContentHash, List<LegalReadinessPlaceholderDto> Placeholders);

public record LegalReadinessUiTextDto(
    string Key, string Version, bool IsDraft, string ContentHash, List<LegalReadinessPlaceholderDto> Placeholders);

public record LegalReadinessBlockerDto(string Kind, string Detail);

public record LegalReadinessImpactDto(string DocumentType, string Gate, int Users);

public record LegalReadinessDto(
    DateTime GeneratedAtUtc, bool Ready, List<LegalReadinessBlockerDto> Blockers,
    List<LegalReadinessDocumentDto> Documents, List<LegalReadinessUiTextDto> UiTexts,
    List<LegalReadinessImpactDto> Impact, string Disclaimer);
