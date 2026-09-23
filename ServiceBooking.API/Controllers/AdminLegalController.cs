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
/// The shared "what counts as a blocker" logic (drafts/placeholders/links/anchors) lives in
/// <see cref="LegalReadinessReportBuilder"/>, not here — <c>ServiceBooking.LegalKit</c>'s `status`/`links`
/// commands call the exact same methods (ARCHITECTURE_CYCLE11.md §105.4/§106.3: one shape, one set of
/// rules, on both sides). This controller only adds the two things a CLI running on bare files cannot
/// produce: `impact` (needs the database) and the HTTP/auth/caching envelope. `drift` (App_Data vs.
/// legal-drafts) is likewise CLI-only — the running container doesn't have `legal-drafts/` on disk — and
/// is intentionally omitted here, which the schema allows since `drift` isn't `required`.
///
/// Known reduction, flagged rather than silently shipped: `placeholders[].valuePresent` requires
/// cross-checking a `legal.values.json` this endpoint doesn't read (that file is CLI-only per
/// ARCHITECTURE_CYCLE11.md §107.1 — "приложению не нужен вовсе"), so it is always reported as `false`
/// here (schema: "без --values всегда false").
/// </summary>
[ApiController]
[Route("api/admin/legal")]
[Authorize(Roles = "SuperAdmin")]
public class AdminLegalController(LegalDocumentProvider legalDocuments, AppDbContext db) : ControllerBase
{
    private const string ImpactNote =
        "Число пользователей, чья последняя зафиксированная версия документа отличается от версии в текущем снимке — " +
        "именно им при публикации будет показан повторный акцепт (или 451 до его прохождения, если gate=Global).";

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
                DateTime.UtcNow, legalDocuments.Root, Ready: false,
                Blockers: [new LegalReadinessBlockerDto("LegalUnavailable", "Снимок правовых документов не загружен.")],
                Documents: [], UiTexts: [], Placeholders: [],
                Links: new LegalReadinessLinksDto(0, []),
                Anchors: new LegalReadinessAnchorsDto([]),
                Impact: new LegalReadinessImpactSummaryDto([], ImpactNote),
                LegalReadinessReportBuilder.Disclaimer));
        }

        var documentReports = LegalReadinessReportBuilder.BuildDocumentReports(snapshot);
        var uiTextReports = LegalReadinessReportBuilder.BuildUiTextReports(snapshot);
        var placeholders = LegalReadinessReportBuilder.BuildPlaceholderSummary(documentReports, uiTextReports);
        var links = LegalReadinessReportBuilder.CheckLinks(snapshot);
        var anchors = LegalReadinessReportBuilder.CheckAnchors(snapshot);
        var blockers = LegalReadinessReportBuilder.BuildBlockers(documentReports, uiTextReports, placeholders, links, anchors);
        var impact = await GetImpactAsync(snapshot, ct);

        return Ok(new LegalReadinessDto(
            DateTime.UtcNow, legalDocuments.Root, Ready: blockers.Count == 0, blockers,
            documentReports, uiTextReports, placeholders, links, anchors, impact, LegalReadinessReportBuilder.Disclaimer));
    }

    /// <summary>ARCHITECTURE_CYCLE11.md §116 `impact`: how many distinct subjects hold a stale accepted
    /// version of each gated document. Computed cold — exactly one query per gated document, never on the
    /// hot path — this endpoint is the only reader. Schema restricts `reAcceptanceRequired[].gate` to
    /// Global/OwnerScope, matching the only two gate values a document requiring re-acceptance can have
    /// (Gate.None is filtered out below, same as before).
    ///
    /// Deliberately not `GroupBy(r => r.UserId).Select(g => g.OrderByDescending(...).First()...)`: EF
    /// Core 8's grouping-operator translation only supports the group key and aggregates
    /// (Count/Sum/Min/Max/Average) in the projection, not an ordered `First()` over the group — that
    /// shape throws `InvalidOperationException` at query time against Npgsql. A prior revision replaced
    /// this with a per-subject loop (1 + N round-trips, N = live consent-record holders — effectively
    /// every registered user for Privacy/TermsClient) which timed out on any non-trivial database. The
    /// translatable, single-round-trip equivalent is a correlated NOT EXISTS: a record is "stale" iff its
    /// version differs from the manifest's current version AND no *newer* current record exists for the
    /// same subject/document (i.e. it IS that subject's latest). This lands on the existing
    /// `IX_ConsentRecords_CurrentByUser (UserId, DocumentKey, Purpose, GrantedAtUtc DESC)
    /// WHERE RevokedAtUtc IS NULL AND UserId IS NOT NULL` index.
    ///
    /// Only *current* (non-revoked) records count towards "holds an accepted version" — this mirrors
    /// <see cref="ConsentLedger"/>'s own `CurrentRecordsQuery` filter, so a subject who revoked consent
    /// isn't miscounted as already covered.</summary>
    private async Task<LegalReadinessImpactSummaryDto> GetImpactAsync(LegalSnapshot snapshot, CancellationToken ct)
    {
        // contracts/cycle11/legal-status.schema.json restricts reAcceptanceRequired[].documentType to
        // exactly {Privacy, TermsClient, TermsOwner} — the three document types LegalGate can plausibly be
        // Global/OwnerScope for today. Filtering on "Gate != None" instead of this explicit allow-list
        // means an unrecognized `gate` string in a hand-edited manifest (LegalDocumentProvider falls back
        // to LegalGate.Global for anything it doesn't recognize) could surface a document type the schema
        // never listed — e.g. PdnConsent with a typo'd gate — and the response would fail its own
        // contract. ARCHITECTURE_CYCLE11.md §111: the schema is the source of truth.
        var reAcceptanceEligibleTypes = new HashSet<LegalDocumentType>
        {
            LegalDocumentType.Privacy, LegalDocumentType.TermsClient, LegalDocumentType.TermsOwner,
        };

        var results = new List<LegalReadinessImpactEntryDto>();
        foreach (var doc in snapshot.Documents.Values
                     .Where(d => reAcceptanceEligibleTypes.Contains(d.Type) && d.Gate != LegalGate.None)
                     .OrderBy(d => d.Type))
        {
            var documentKey = doc.Type.ToString();

            var currentRecordsQuery = db.ConsentRecords
                .Where(r => r.DocumentKey == documentKey && r.UserId != null && r.RevokedAtUtc == null);

            // A subject's latest current record is stale when its version isn't the manifest's current
            // version. "Latest" is expressed as "no newer current record exists for the same subject",
            // which EF Core 8 translates as a single correlated NOT EXISTS subquery.
            var staleCount = await currentRecordsQuery
                .Where(r => r.DocumentVersion != doc.Version
                    && !currentRecordsQuery.Any(o => o.UserId == r.UserId && o.GrantedAtUtc > r.GrantedAtUtc))
                .Select(r => r.UserId!)
                .Distinct()
                .CountAsync(ct);

            if (staleCount > 0)
                results.Add(new LegalReadinessImpactEntryDto(documentKey, doc.Gate.ToString(), staleCount));
        }
        return new LegalReadinessImpactSummaryDto(results, ImpactNote);
    }
}

public record LegalReadinessDto(
    DateTime GeneratedAtUtc, string Root, bool Ready, List<LegalReadinessBlockerDto> Blockers,
    List<LegalReadinessDocumentDto> Documents, List<LegalReadinessUiTextDto> UiTexts,
    List<LegalReadinessPlaceholderSummaryDto> Placeholders, LegalReadinessLinksDto Links,
    LegalReadinessAnchorsDto Anchors, LegalReadinessImpactSummaryDto Impact, string Disclaimer);
