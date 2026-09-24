using System.Text.RegularExpressions;
using System.Text.Json;

namespace ServiceBooking.LegalKit;

/// <summary>
/// Turns <c>legal-drafts/</c> into <c>ServiceBooking.API/App_Data/legal/</c> (ARCHITECTURE_CYCLE11.md
/// §103.2/§105.1). Deterministic: same input bytes in ⇒ same output bytes out — no timestamps, no
/// machine-dependent ordering, line endings always normalized to <c>\n</c>. The non-trivial steps are:
///  - splicing zero or more appendix bodies into a document at the
///    <c>&lt;!-- APPENDIX-BODY-START --&gt;</c> marker, in the ORDER the manifest's <c>appendices</c>
///    list gives them (cycle 12 review finding 4.1 — this used to be a single hard-coded file name and
///    a splice method with a docstring-only ordering rule; a second appendix could be wired in the wrong
///    order with nothing catching it);
///  - stripping internal `ЮРИСТУ:`/`РАЗРАБОТКЕ:` HTML comments out of the artifact the browser actually
///    receives (cycle 12 review finding, top priority) — the SOURCE keeps them, the ARTIFACT must not.
/// </summary>
internal static class LegalSourceSet
{
    public const string AppendixBodyMarker = "<!-- APPENDIX-BODY-START -->";
    private const string ManifestFileName = "legal.json";

    public sealed record ManifestFileEntry(string Kind, string Type, string File);

    /// <summary>One entry of the manifest's <c>deferredDrafts</c> list: a file sitting in the source
    /// directory that is deliberately NOT part of the artifact yet, plus why.</summary>
    public sealed record DeferredDraftEntry(string File, string Reason);

    /// <summary>Builds the artifact into <paramref name="outDir"/> (created if missing). Throws
    /// <see cref="InvalidOperationException"/> with a clear message on any structural problem — a marker
    /// missing from an appendix draft, a manifest entry pointing at a file that doesn't exist, etc.
    /// Does NOT validate placeholders/business rules beyond what
    /// <see cref="ServiceBooking.API.Services.Legal.LegalDocumentProvider.LoadStrict"/> itself enforces —
    /// callers are expected to run that against the result (see <see cref="Commands.BuildCommand"/>).</summary>
    public static void Build(string sourceDir, string outDir)
    {
        var manifestPath = Path.Combine(sourceDir, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Манифест не найден: {manifestPath}");

        var manifestBytes = NormalizeLineEndings(File.ReadAllText(manifestPath));
        var entries = ParseManifestEntries(manifestBytes);
        var appendices = ParseAppendices(manifestBytes);

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, ManifestFileName), manifestBytes);

        foreach (var entry in entries)
        {
            var sourcePath = Path.Combine(sourceDir, entry.File);
            if (!File.Exists(sourcePath))
                throw new InvalidOperationException(
                    $"{(string.IsNullOrEmpty(entry.Type) ? entry.Kind : entry.Type)}: файл '{entry.File}' не найден в {sourceDir}.");

            var content = NormalizeLineEndings(File.ReadAllText(sourcePath));

            if (appendices.TryGetValue(entry.Type, out var appendixFiles))
                content = SpliceAppendices(sourceDir, entry.Type, content, appendixFiles);

            content = StripHtmlComments(content);

            File.WriteAllText(Path.Combine(outDir, entry.File), content);
        }
    }

    /// <summary>Files a fully built artifact is expected to contain: the manifest plus every file a
    /// document/uiText entry references. Used by `legal check`'s byte comparison and `status`'s `drift`
    /// block — both need to know exactly which files are "the artifact", not everything sitting in the
    /// directory.</summary>
    public static IReadOnlyList<string> EnumerateManifestFiles(string sourceDir)
    {
        var manifestPath = Path.Combine(sourceDir, ManifestFileName);
        if (!File.Exists(manifestPath)) return [ManifestFileName];
        var entries = ParseManifestEntries(File.ReadAllText(manifestPath));
        return new[] { ManifestFileName }.Concat(entries.Select(e => e.File)).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every top-level <c>*.html</c> file sitting in <paramref name="dir"/> that the manifest at
    /// <paramref name="sourceDir"/> does NOT produce — e.g. cycle 5's <c>privacy.html</c>/<c>terms.html</c>
    /// stubs orphaned by cycle 11 renaming the artifact's files to <c>01-privacy-policy.html</c> etc.
    /// (ARCHITECTURE_CYCLE11.md §103.2: the artifact is "11 files under the same names" — an exact set,
    /// not a minimum). Scoped to <c>*.html</c> only, and to the top level only: anything else that
    /// happens to live in the artifact directory (a `.gitkeep`, a README, a subdirectory) is left alone
    /// on purpose — a build/check tool that also reasons about arbitrary non-artifact files is exactly
    /// the kind of "just delete everything and start over" behavior that's unsafe for a directory that's
    /// tracked in git.</summary>
    public static IReadOnlyList<string> FindStrayHtmlFiles(string dir, string sourceDir)
    {
        if (!Directory.Exists(dir)) return [];
        var expected = EnumerateManifestFiles(sourceDir).ToHashSet(StringComparer.Ordinal);
        return Directory.EnumerateFiles(dir, "*.html", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !expected.Contains(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Cycle 12 review finding 3.1: every <c>*.html</c> file sitting at the top level of
    /// <paramref name="sourceDir"/> must be accounted for by one of three things — a manifest entry, an
    /// appendix body some manifest entry splices in, or an explicit <c>deferredDrafts</c> entry with a
    /// reason. A file that is none of these (the "someone added
    /// <c>legal-drafts/14-something.html</c> and forgot to wire it in" scenario) is returned here so
    /// `legal check` can refuse instead of silently shipping a draft nobody referenced.</summary>
    public static IReadOnlyList<string> FindUnaccountedDraftFiles(string sourceDir)
    {
        var manifestPath = Path.Combine(sourceDir, ManifestFileName);
        if (!File.Exists(manifestPath)) return [];
        var manifestJson = File.ReadAllText(manifestPath);

        var accounted = ParseManifestEntries(manifestJson).Select(e => e.File)
            .Concat(ParseAppendices(manifestJson).Values.SelectMany(files => files))
            .Concat(ParseDeferredDrafts(manifestJson).Select(d => d.File))
            .ToHashSet(StringComparer.Ordinal);

        if (!Directory.Exists(sourceDir)) return [];
        return Directory.EnumerateFiles(sourceDir, "*.html", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !accounted.Contains(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The manifest's <c>deferredDrafts</c> list, parsed for callers (`legal check`, `legal
    /// status`) that need to report WHY a file is intentionally excluded, not just that it is.</summary>
    public static IReadOnlyList<DeferredDraftEntry> ParseDeferredDrafts(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var result = new List<DeferredDraftEntry>();
        if (!doc.RootElement.TryGetProperty("deferredDrafts", out var deferred) || deferred.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var d in deferred.EnumerateArray())
        {
            var file = d.GetProperty("file").GetString() ?? "";
            var reason = d.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            result.Add(new DeferredDraftEntry(file, reason));
        }
        return result;
    }

    private static string SpliceAppendices(string sourceDir, string documentType, string content, IReadOnlyList<string> appendixFiles)
    {
        foreach (var appendixFile in appendixFiles)
        {
            var appendixPath = Path.Combine(sourceDir, appendixFile);
            if (!File.Exists(appendixPath))
                throw new InvalidOperationException(
                    $"{documentType}: приложение не найдено ({appendixFile} отсутствует в {sourceDir}).");

            var appendixContent = NormalizeLineEndings(File.ReadAllText(appendixPath));
            var markerIndex = appendixContent.IndexOf(AppendixBodyMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
                throw new InvalidOperationException(
                    $"{appendixFile}: не найдена метка '{AppendixBodyMarker}' — склейка в {documentType} невозможна " +
                    "(ARCHITECTURE_CYCLE11.md §103.2).");

            var afterMarker = appendixContent[(markerIndex + AppendixBodyMarker.Length)..].TrimStart('\n', '\r');
            var separator = content.EndsWith('\n') ? "" : "\n";
            content += separator + afterMarker;
        }

        return content;
    }

    /// <summary>Matches an entire HTML comment, including internal newlines. Applied to the ARTIFACT only
    /// (never to <c>legal-drafts/*.html</c> themselves) — cycle 12 review finding: the browser-facing
    /// build must not ship `ЮРИСТУ:`/`РАЗРАБОТКЕ:` review notes as visible page-source comments. Runs
    /// AFTER splicing so <see cref="AppendixBodyMarker"/> — itself an HTML comment — is still there for
    /// <see cref="SpliceAppendices"/> to find; only the surviving assembled content gets stripped.</summary>
    private static readonly Regex HtmlCommentRegex = new("<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Strips HTML comments, then collapses the runs of blank lines the removal leaves behind
    /// so the artifact stays readable rather than full of holes. Pure text transform — deterministic,
    /// same bytes in ⇒ same bytes out, no environment dependence, so `legal check`'s byte-for-byte
    /// comparison (which rebuilds from source and re-runs this exact function) stays meaningful.</summary>
    internal static string StripHtmlComments(string html)
    {
        var withoutComments = HtmlCommentRegex.Replace(html, "");

        var collapsed = new List<string>();
        var previousBlank = false;
        foreach (var rawLine in withoutComments.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var isBlank = line.Length == 0;
            if (isBlank && previousBlank) continue;
            collapsed.Add(line);
            previousBlank = isBlank;
        }

        return string.Join('\n', collapsed);
    }

    private static List<ManifestFileEntry> ParseManifestEntries(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var entries = new List<ManifestFileEntry>();

        if (doc.RootElement.TryGetProperty("documents", out var documents) && documents.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in documents.EnumerateArray())
            {
                var type = d.GetProperty("type").GetString() ?? "";
                var file = d.GetProperty("file").GetString() ?? "";
                entries.Add(new ManifestFileEntry("document", type, file));
            }
        }

        if (doc.RootElement.TryGetProperty("uiTexts", out var uiTexts) && uiTexts.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in uiTexts.EnumerateArray())
            {
                var key = t.GetProperty("key").GetString() ?? "";
                var file = t.GetProperty("file").GetString() ?? "";
                entries.Add(new ManifestFileEntry("uiText", key, file));
            }
        }

        return entries;
    }

    /// <summary>The manifest's <c>appendices</c> object: document `type` → ordered list of appendix file
    /// names to splice into that document, in list order (cycle 12 review finding 4.1 — order is now
    /// data, not a hard-coded "file 13 after file 09" rule in prose). Absent entirely on a manifest with
    /// no appendices at all (kept backward compatible with the pre-cycle-12 shape).</summary>
    private static Dictionary<string, List<string>> ParseAppendices(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!doc.RootElement.TryGetProperty("appendices", out var appendices) || appendices.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in appendices.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : [];
        }
        return result;
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");
}
