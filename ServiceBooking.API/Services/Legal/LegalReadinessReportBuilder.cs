using System.Text.RegularExpressions;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// The one place that turns a <see cref="LegalSnapshot"/> into the shape
/// <c>contracts/cycle11/legal-status.schema.json</c> describes (ARCHITECTURE_CYCLE11.md §105.4:
/// "один и тот же JSON отдают двое"). <see cref="ServiceBooking.API.Controllers.AdminLegalController"/>
/// (in-process, has a database for `impact`) and <c>ServiceBooking.LegalKit</c>'s `status`/`links`
/// commands (out-of-process, has a filesystem for `drift`) both call into this class rather than each
/// growing their own copy of "what counts as a broken link" or "what counts as an unresolved
/// placeholder" — ARCHITECTURE_CYCLE11.md §106.3/§104.1's "second implementation of the rules physically
/// doesn't exist" applies here exactly as much as it does to <see cref="LegalDocumentProvider.LoadStrict"/>.
/// </summary>
public static class LegalReadinessReportBuilder
{
    public const string Disclaimer =
        "Проверено механически. Это НЕ юридическая вычитка: проверка ловит незаполненные плейсхолдеры, " +
        "битые ссылки и неполный комплект, но не ловит неверный по сути текст. Публикация требует обоих подтверждений.";

    /// <summary>Only http(s):// absolute links are out of scope for internal link checking — everything
    /// else starting with '/' must resolve to a known route, alias, or in-page anchor of one.</summary>
    private static readonly Regex HrefRegex =
        new("""href\s*=\s*["'](?<href>/[^"'#]*(?:#[^"']*)?)["']""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>LEGAL_REVIEW.md §13-бис's own grouping of the 13 `legal-values.schema.json` placeholder
    /// names plus the 2 manifest-derived ones — the schema's `source` enum is that grouping verbatim, and
    /// it's a static fact about the document set (which fact never changes without a legal review), not
    /// something derived at runtime. Any placeholder name not in this map is new/unclassified and falls
    /// back to "из манифеста", the schema's own bucket for "we don't know the real source".</summary>
    public static readonly IReadOnlyDictionary<string, string> PlaceholderSources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["НАИМЕНОВАНИЕ_ОПЕРАТОРА"] = "ЕГРЮЛ",
        ["ИНН_ОПЕРАТОРА"] = "ЕГРЮЛ",
        ["ОГРН_ОПЕРАТОРА"] = "ЕГРЮЛ",
        ["ЮРИДИЧЕСКИЙ_АДРЕС"] = "ЕГРЮЛ",
        ["НОМЕР_УВЕДОМЛЕНИЯ_РКН"] = "после уведомления РКН",
        ["ДАТА_УВЕДОМЛЕНИЯ_РКН"] = "после уведомления РКН",
        ["ПОЧТОВЫЙ_АДРЕС"] = "решение заказчика",
        ["ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ"] = "решение заказчика",
        ["ТЕЛЕФОН_ОПЕРАТОРА"] = "решение заказчика",
        ["ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ"] = "решение заказчика",
        ["ПОЧТА_ОТВЕТСТВЕННОГО"] = "решение заказчика",
        ["СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ"] = "решение заказчика",
        ["НДС_ОГОВОРКА"] = "решение заказчика",
        ["ВЕРСИЯ_ДОКУМЕНТА"] = "из манифеста",
        ["ДАТА_ВСТУПЛЕНИЯ_В_СИЛУ"] = "из манифеста",
    };

    public static LegalReadinessDocumentDto BuildDocumentReport(LegalDocument d) =>
        new(d.Type.ToString(), d.Title, d.Version, d.EffectiveFrom, d.IsDraft, d.ChangeKind.ToString(),
            d.Gate.ToString(), d.File, LegalRoutes.UrlFor(d.Type), d.ContentHash, FindPlaceholders(d.ContentHtml));

    public static LegalReadinessUiTextDto BuildUiTextReport(LegalUiText t) =>
        new(t.Key, t.Version, t.IsDraft, t.File, t.ContentHash, FindPlaceholders(t.ContentHtml));

    public static List<LegalReadinessDocumentDto> BuildDocumentReports(LegalSnapshot snapshot) =>
        snapshot.Documents.Values.OrderBy(d => d.Type).Select(BuildDocumentReport).ToList();

    public static List<LegalReadinessUiTextDto> BuildUiTextReports(LegalSnapshot snapshot) =>
        snapshot.UiTexts.Values.OrderBy(t => t.Key, StringComparer.Ordinal).Select(BuildUiTextReport).ToList();

    public static List<LegalReadinessPlaceholderDto> FindPlaceholders(string html)
    {
        var matches = Regex.Matches(html, LegalDocumentProvider.PlaceholderPattern);
        return matches.Select(m => m.Value.Trim('{', '}'))
            .GroupBy(name => name, StringComparer.Ordinal)
            .Select(g => new LegalReadinessPlaceholderDto(g.Key, g.Count()))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Sums per-document/uiText placeholder hits into the schema's summary shape. `source` is
    /// looked up from <see cref="PlaceholderSources"/> (LEGAL_REVIEW.md §13-бис); `valuePresent` is filled
    /// in by the caller when a `legal.values.json` is available (CLI's `status --values`), and is
    /// always `false` without one — "отсутствие файла значений — это отсутствие значений, а не
    /// неизвестность" (legal-status.schema.json).</summary>
    public static List<LegalReadinessPlaceholderSummaryDto> BuildPlaceholderSummary(
        List<LegalReadinessDocumentDto> documents, List<LegalReadinessUiTextDto> uiTexts,
        IReadOnlySet<string>? presentValueNames = null)
    {
        var hits = documents.SelectMany(d => d.Placeholders.Select(p => (p.Name, p.Count, File: d.File)))
            .Concat(uiTexts.SelectMany(t => t.Placeholders.Select(p => (p.Name, p.Count, File: t.File))));

        return hits.GroupBy(h => h.Name, StringComparer.Ordinal)
            .Select(g => new LegalReadinessPlaceholderSummaryDto(
                g.Key, g.Sum(h => h.Count),
                PlaceholderSources.GetValueOrDefault(g.Key, "из манифеста"),
                g.Select(h => h.File).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList(),
                ValuePresent: presentValueNames?.Contains(g.Key) ?? false))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every internal (site-relative) href found in any document/uiText body must resolve to a
    /// known SPA route (LegalRoutes.Documents) or a known alias (LegalRoutes.Aliases) — anything else is
    /// a broken link (closes SPEC's link-check requirement without a filesystem/HTTP crawl).</summary>
    public static LegalReadinessLinksDto CheckLinks(LegalSnapshot snapshot)
    {
        var validTargets = LegalRoutes.Documents.Values
            .Concat(LegalRoutes.Aliases.Keys)
            .Select(r => r.Split('#')[0])
            .ToHashSet(StringComparer.Ordinal);

        var broken = new List<LegalReadinessBrokenLinkDto>();
        var checkedCount = 0;

        foreach (var (file, html) in AllSources(snapshot))
        {
            foreach (Match m in HrefRegex.Matches(html))
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
    public static LegalReadinessAnchorsDto CheckAnchors(LegalSnapshot snapshot)
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

    /// <summary>Same "Draft → Placeholders → Links → Anchors" order the endpoint has always produced,
    /// pulled out so the CLI's `status` reports blockers in an identical order (drift/impact are
    /// caller-specific and appended by the caller, not here).</summary>
    public static List<LegalReadinessBlockerDto> BuildBlockers(
        List<LegalReadinessDocumentDto> documents, List<LegalReadinessUiTextDto> uiTexts,
        List<LegalReadinessPlaceholderSummaryDto> placeholders, LegalReadinessLinksDto links, LegalReadinessAnchorsDto anchors)
    {
        var blockers = new List<LegalReadinessBlockerDto>();

        var draftDocuments = documents.Where(d => d.IsDraft).ToList();
        var draftUiTexts = uiTexts.Where(t => t.IsDraft).ToList();
        if (draftDocuments.Count > 0 || draftUiTexts.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto(
                "DraftDocuments", $"{draftDocuments.Count} документов и {draftUiTexts.Count} текстов интерфейса"));

        if (placeholders.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto(
                "UnresolvedPlaceholders", $"{placeholders.Count} видов, {placeholders.Sum(p => p.Count)} вхождений"));

        if (links.Broken.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto("BrokenLinks", $"{links.Broken.Count} битых ссылок"));

        if (anchors.Missing.Count > 0)
            blockers.Add(new LegalReadinessBlockerDto("MissingAnchors", $"{anchors.Missing.Count} отсутствующих якорей"));

        return blockers;
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

public record LegalReadinessDriftDto(string ComparedWith, List<string> DifferentFiles);
