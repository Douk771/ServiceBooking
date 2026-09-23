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
/// endpoint now populates every field the schema marks `required` (contract re-check, cycle 11, round 2):
/// `root` from the provider's actual configured directory, `file` on every document/uiText entry, `links`
/// and `anchors` computed from `LegalRoutes` against the in-memory HTML (no filesystem walk — the CLI's
/// eventual `drift` field, which DOES need filesystem comparison against `legal-drafts`, is still out of
/// scope here and is intentionally omitted, which the schema allows since `drift` isn't `required`).
/// `impact` is the one field the schema documents as endpoint-only (the CLI has no database).
///
/// Known reduction, flagged rather than silently shipped: `placeholders[].source`/`valuePresent` require
/// classifying each placeholder name against LEGAL_REVIEW.md §13-бис and cross-checking a
/// `legal.values.json` this codebase doesn't read. Absent that file, every placeholder is reported with
/// `source: "из манифеста"` (the schema's own fallback bucket for "we don't know the real source") and
/// `valuePresent: false` (schema: "без --values всегда false"), which is honest but coarser than the full
/// CLI report would be once T1/T3 exist.
/// </summary>
[ApiController]
[Route("api/admin/legal")]
[Authorize(Roles = "SuperAdmin")]
public class AdminLegalController(LegalDocumentProvider legalDocuments, AppDbContext db) : ControllerBase
{
    private const string Disclaimer =
        "Проверено механически. Это НЕ юридическая вычитка: проверка ловит незаполненные плейсхолдеры, " +
        "битые ссылки и неполный комплект, но не ловит неверный по сути текст. Публикация требует обоих подтверждений.";

    private const string ImpactNote =
        "Число пользователей, чья последняя зафиксированная версия документа отличается от версии в текущем снимке — " +
        "именно им при публикации будет показан повторный акцепт (или 451 до его прохождения, если gate=Global).";

    /// <summary>Only http(s):// absolute links are out of scope for internal link checking — everything
    /// else starting with '/' must resolve to a known route, alias, or in-page anchor of one.</summary>
    private static readonly System.Text.RegularExpressions.Regex HrefRegex =
        new("""href\s*=\s*["'](?<href>/[^"'#]*(?:#[^"']*)?)["']""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

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
                Disclaimer));
        }

        var documentReports = snapshot.Documents.Values.OrderBy(d => d.Type)
            .Select(d => new LegalReadinessDocumentDto(
                d.Type.ToString(), d.Title, d.Version, d.EffectiveFrom, d.IsDraft, d.ChangeKind.ToString(),
                d.Gate.ToString(), d.File, LegalRoutes.UrlFor(d.Type), d.ContentHash, FindPlaceholders(d.ContentHtml)))
            .ToList();

        var uiTextReports = snapshot.UiTexts.Values.OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new LegalReadinessUiTextDto(t.Key, t.Version, t.IsDraft, t.File, t.ContentHash, FindPlaceholders(t.ContentHtml)))
            .ToList();

        var blockers = new List<LegalReadinessBlockerDto>();

        var draftDocuments = documentReports.Where(d => d.IsDraft).ToList();
        var draftUiTexts = uiTextReports.Where(t => t.IsDraft).ToList();
        if (draftDocuments.Count > 0 || draftUiTexts.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto(
                "DraftDocuments", $"{draftDocuments.Count} документов и {draftUiTexts.Count} текстов интерфейса"));

        var placeholders = BuildPlaceholderSummary(documentReports, uiTextReports);
        if (placeholders.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto(
                "UnresolvedPlaceholders", $"{placeholders.Count} видов, {placeholders.Sum(p => p.Count)} вхождений"));

        var links = CheckLinks(snapshot);
        if (links.Broken.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto("BrokenLinks", $"{links.Broken.Count} битых ссылок"));

        var anchors = CheckAnchors(snapshot);
        if (anchors.Missing.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto("MissingAnchors", $"{anchors.Missing.Count} отсутствующих якорей"));

        var impact = await GetImpactAsync(snapshot, ct);

        return Ok(new LegalReadinessDto(
            DateTime.UtcNow, legalDocuments.Root, Ready: blockers.Count == 0, blockers,
            documentReports, uiTextReports, placeholders, links, anchors, impact, Disclaimer));
    }

    /// <summary>ARCHITECTURE_CYCLE11.md §116 `impact`: how many distinct subjects hold a stale accepted
    /// version of each gated document. Computed cold (one grouped query per gated document), never on
    /// the hot path — this endpoint is the only reader. Schema restricts `reAcceptanceRequired[].gate` to
    /// Global/OwnerScope, matching the only two gate values a document requiring re-acceptance can have
    /// (Gate.None is filtered out below, same as before).</summary>
    private async Task<LegalReadinessImpactSummaryDto> GetImpactAsync(LegalSnapshot snapshot, CancellationToken ct)
    {
        var results = new List<LegalReadinessImpactEntryDto>();
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
                results.Add(new LegalReadinessImpactEntryDto(documentKey, doc.Gate.ToString(), staleCount));
        }
        return new LegalReadinessImpactSummaryDto(results, ImpactNote);
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

    /// <summary>Sums per-document/uiText placeholder hits into the schema's summary shape. See the class
    /// doc-comment: `source`/`valuePresent` are a known, flagged reduction without a `legal.values.json`
    /// reader in this codebase.</summary>
    internal static List<LegalReadinessPlaceholderSummaryDto> BuildPlaceholderSummary(
        List<LegalReadinessDocumentDto> documents, List<LegalReadinessUiTextDto> uiTexts)
    {
        var hits = documents.SelectMany(d => d.Placeholders.Select(p => (p.Name, p.Count, File: d.File)))
            .Concat(uiTexts.SelectMany(t => t.Placeholders.Select(p => (p.Name, p.Count, File: t.File))));

        return hits.GroupBy(h => h.Name, StringComparer.Ordinal)
            .Select(g => new LegalReadinessPlaceholderSummaryDto(
                g.Key, g.Sum(h => h.Count), "из манифеста",
                g.Select(h => h.File).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList(),
                ValuePresent: false))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every internal (site-relative) href found in any document/uiText body must resolve to a
    /// known SPA route (LegalRoutes.Documents) or a known alias (LegalRoutes.Aliases) — anything else is
    /// a broken link (closes SPEC's link-check requirement without a filesystem/HTTP crawl).</summary>
    internal static LegalReadinessLinksDto CheckLinks(LegalSnapshot snapshot)
    {
        var validTargets = LegalRoutes.Documents.Values
            .Concat(LegalRoutes.Aliases.Keys)
            .Select(r => r.Split('#')[0])
            .ToHashSet(StringComparer.Ordinal);

        var broken = new List<LegalReadinessBrokenLinkDto>();
        var checkedCount = 0;

        foreach (var (file, html) in AllSources(snapshot))
        {
            foreach (System.Text.RegularExpressions.Match m in HrefRegex.Matches(html))
            {
                checkedCount++;
                var href = m.Groups["href"].Value;
                var path = href.Split('#')[0];
                if (!validTargets.Contains(path))
                    broken.Add(new LegalReadinessBrokenLinkDto(file, href));
            }
        }

        return new LegalReadinessLinksDto(checkedCount, broken);
    }

    /// <summary>Every route→anchor requirement in LegalRoutes.Anchors must have a matching
    /// id="anchor"/name="anchor" in that route's document HTML.</summary>
    internal static LegalReadinessAnchorsDto CheckAnchors(LegalSnapshot snapshot)
    {
        var byRoute = LegalRoutes.Documents.ToDictionary(kv => kv.Value, kv => snapshot.Get(kv.Key));

        var missing = new List<LegalReadinessMissingAnchorDto>();
        foreach (var (route, requiredAnchors) in LegalRoutes.Anchors)
        {
            var doc = byRoute.GetValueOrDefault(route);
            var html = doc?.ContentHtml ?? "";
            foreach (var anchor in requiredAnchors)
            {
                var hasAnchor = html.Contains($"id=\"{anchor}\"", StringComparison.OrdinalIgnoreCase)
                    || html.Contains($"id='{anchor}'", StringComparison.OrdinalIgnoreCase)
                    || html.Contains($"name=\"{anchor}\"", StringComparison.OrdinalIgnoreCase)
                    || html.Contains($"name='{anchor}'", StringComparison.OrdinalIgnoreCase);
                if (!hasAnchor)
                    missing.Add(new LegalReadinessMissingAnchorDto(route, anchor));
            }
        }
        return new LegalReadinessAnchorsDto(missing);
    }

    private static IEnumerable<(string File, string Html)> AllSources(LegalSnapshot snapshot) =>
        snapshot.Documents.Values.Select(d => (d.File, d.ContentHtml))
            .Concat(snapshot.UiTexts.Values.Select(t => (t.File, t.ContentHtml)));
}

public record LegalReadinessPlaceholderDto(string Name, int Count);

public record LegalReadinessDocumentDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind,
    string Gate, string File, string Url, string ContentHash, List<LegalReadinessPlaceholderDto> Placeholders);

public record LegalReadinessUiTextDto(
    string Key, string Version, bool IsDraft, string File, string ContentHash, List<LegalReadinessPlaceholderDto> Placeholders);

public record LegalReadinessBlockerDto(string Kind, string Detail);

public record LegalReadinessPlaceholderSummaryDto(
    string Name, int Count, string Source, List<string> Files, bool ValuePresent);

public record LegalReadinessBrokenLinkDto(string File, string Href);

public record LegalReadinessLinksDto(int Checked, List<LegalReadinessBrokenLinkDto> Broken);

public record LegalReadinessMissingAnchorDto(string Route, string Anchor);

public record LegalReadinessAnchorsDto(List<LegalReadinessMissingAnchorDto> Missing);

public record LegalReadinessImpactEntryDto(string DocumentType, string Gate, int Users);

public record LegalReadinessImpactSummaryDto(List<LegalReadinessImpactEntryDto> ReAcceptanceRequired, string Note);

public record LegalReadinessDto(
    DateTime GeneratedAtUtc, string Root, bool Ready, List<LegalReadinessBlockerDto> Blockers,
    List<LegalReadinessDocumentDto> Documents, List<LegalReadinessUiTextDto> UiTexts,
    List<LegalReadinessPlaceholderSummaryDto> Placeholders, LegalReadinessLinksDto Links,
    LegalReadinessAnchorsDto Anchors, LegalReadinessImpactSummaryDto Impact, string Disclaimer);
