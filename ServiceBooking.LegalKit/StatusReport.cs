using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.LegalKit;

/// <summary>
/// Builds the one report shape both `legal status --json` and `GET /api/admin/legal/readiness` produce
/// (ARCHITECTURE_CYCLE11.md §105.4, contracts/cycle11/legal-status.schema.json). Reuses
/// <see cref="LegalReadinessReportBuilder"/> — the exact same static methods
/// <c>AdminLegalController</c> calls — for every concern the two sides share (drafts, placeholders,
/// links, anchors). The CLI adds `drift` (App_Data vs. legal-drafts), which the endpoint can't compute
/// (no `legal-drafts/` inside the running container); it never adds `impact` (needs the database).
/// </summary>
internal static class StatusReport
{
    /// <summary>Builds the report for a loaded snapshot. <paramref name="valuesPath"/>, when given, marks
    /// each placeholder's <c>valuePresent</c> against that file — an invalid/missing file is reported
    /// once as a <c>MissingValues</c> blocker rather than failing the whole command (status is read-only
    /// diagnostics, not `publish`'s stricter gate).</summary>
    public static JsonObject Build(LegalSnapshot snapshot, string root, string? sourceDirForDrift, string? valuesPath)
    {
        var documentReports = LegalReadinessReportBuilder.BuildDocumentReports(snapshot);
        var uiTextReports = LegalReadinessReportBuilder.BuildUiTextReports(snapshot);

        IReadOnlySet<string>? presentValueNames = null;
        var blockers = new List<LegalReadinessBlockerDto>();

        if (valuesPath is not null)
        {
            try
            {
                var values = PlaceholderValues.Load(valuesPath);
                presentValueNames = values.Values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key)
                    .ToHashSet(StringComparer.Ordinal);
            }
            catch (PlaceholderValuesException ex)
            {
                blockers.Add(new LegalReadinessBlockerDto("MissingValues", string.Join("; ", ex.Problems)));
            }
        }

        var placeholders = LegalReadinessReportBuilder.BuildPlaceholderSummary(documentReports, uiTextReports, presentValueNames);
        var links = LegalReadinessReportBuilder.CheckLinks(snapshot);
        var anchors = LegalReadinessReportBuilder.CheckAnchors(snapshot);
        blockers.AddRange(LegalReadinessReportBuilder.BuildBlockers(documentReports, uiTextReports, placeholders, links, anchors));

        JsonObject? driftNode = null;
        if (sourceDirForDrift is not null)
        {
            var differentFiles = ComputeDrift(sourceDirForDrift, root);
            driftNode = new JsonObject
            {
                ["comparedWith"] = sourceDirForDrift,
                ["differentFiles"] = new JsonArray(differentFiles.Select(f => (JsonNode)f).ToArray()),
            };
            if (differentFiles.Count > 0)
                blockers.Add(new LegalReadinessBlockerDto("ArtifactDrift", $"{differentFiles.Count} файлов отличается от {sourceDirForDrift}"));
        }

        var root_ = new JsonObject
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["root"] = root,
            ["ready"] = blockers.Count == 0,
            ["blockers"] = ToArray(blockers, b => new JsonObject { ["kind"] = b.Kind, ["detail"] = b.Detail }),
            ["documents"] = ToArray(documentReports, DocumentToJson),
            ["uiTexts"] = ToArray(uiTextReports, UiTextToJson),
            ["placeholders"] = ToArray(placeholders, PlaceholderSummaryToJson),
            ["links"] = new JsonObject
            {
                ["checked"] = links.Checked,
                ["broken"] = ToArray(links.Broken, b => new JsonObject { ["file"] = b.File, ["href"] = b.Href }),
            },
            ["anchors"] = new JsonObject
            {
                ["missing"] = ToArray(anchors.Missing, m => new JsonObject { ["route"] = m.Route, ["anchor"] = m.Anchor }),
            },
            ["disclaimer"] = LegalReadinessReportBuilder.Disclaimer,
        };

        if (driftNode is not null)
            root_["drift"] = driftNode;

        return root_;
    }

    /// <summary>Byte-for-byte comparison of every manifest-referenced file between a freshly-built
    /// artifact (from <paramref name="sourceDir"/>) and <paramref name="root"/> — this is `legal check`'s
    /// core and `status`'s `drift` block (ARCHITECTURE_CYCLE11.md §105.2/§105.4, risk A6). Missing files
    /// on either side count as a difference.</summary>
    public static List<string> ComputeDrift(string sourceDir, string root)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "legalkit-drift-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(sourceDir, tempDir);
            var files = LegalSourceSet.EnumerateManifestFiles(sourceDir);
            var different = new List<string>();
            foreach (var file in files)
            {
                var builtPath = Path.Combine(tempDir, file);
                var committedPath = Path.Combine(root, file);
                var builtExists = File.Exists(builtPath);
                var committedExists = File.Exists(committedPath);
                if (builtExists != committedExists || (builtExists && File.ReadAllText(builtPath) != File.ReadAllText(committedPath)))
                    different.Add(file);
            }
            // A file `root` still has but the build no longer produces (§103.2: the artifact is an
            // exact set, not a floor) is drift too — same reasoning as `CheckCommand`.
            different.AddRange(LegalSourceSet.FindStrayHtmlFiles(root, sourceDir));
            return different;
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static JsonArray ToArray<T>(IEnumerable<T> items, Func<T, JsonNode> map) =>
        new(items.Select(map).ToArray());

    private static JsonObject DocumentToJson(LegalReadinessDocumentDto d) => new()
    {
        ["type"] = d.Type,
        ["title"] = d.Title,
        ["version"] = d.Version,
        ["effectiveFrom"] = d.EffectiveFrom.ToString("yyyy-MM-dd"),
        ["isDraft"] = d.IsDraft,
        ["changeKind"] = d.ChangeKind,
        ["gate"] = d.Gate,
        ["file"] = d.File,
        ["url"] = d.Url,
        ["contentHash"] = d.ContentHash,
        ["placeholders"] = ToArray(d.Placeholders, PlaceholderHitToJson),
    };

    private static JsonObject UiTextToJson(LegalReadinessUiTextDto t) => new()
    {
        ["key"] = t.Key,
        ["version"] = t.Version,
        ["isDraft"] = t.IsDraft,
        ["file"] = t.File,
        ["contentHash"] = t.ContentHash,
        ["placeholders"] = ToArray(t.Placeholders, PlaceholderHitToJson),
    };

    private static JsonObject PlaceholderHitToJson(LegalReadinessPlaceholderDto p) => new()
    {
        ["name"] = p.Name,
        ["count"] = p.Count,
    };

    private static JsonObject PlaceholderSummaryToJson(LegalReadinessPlaceholderSummaryDto p) => new()
    {
        ["name"] = p.Name,
        ["count"] = p.Count,
        ["source"] = p.Source,
        ["files"] = new JsonArray(p.Files.Select(f => (JsonNode)f).ToArray()),
        ["valuePresent"] = p.ValuePresent,
    };

    private static readonly JsonSerializerOptions PrintOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJsonString(JsonObject obj) => obj.ToJsonString(PrintOptions);
}
