using System.Text;
using System.Text.Json;

namespace ServiceBooking.LegalKit;

/// <summary>
/// Turns <c>legal-drafts/</c> into <c>ServiceBooking.API/App_Data/legal/</c> (ARCHITECTURE_CYCLE11.md
/// §103.2/§105.1). Deterministic: same input bytes in ⇒ same output bytes out — no timestamps, no
/// machine-dependent ordering, line endings always normalized to <c>\n</c>. The one non-trivial step is
/// splicing <c>09-channel-offer.html</c>'s body into <c>03-terms-owner.html</c> at the
/// <c>&lt;!-- APPENDIX-BODY-START --&gt;</c> marker (§103.2) — everything else is a byte-for-byte copy
/// under the SAME filename the source manifest already uses (the diagram in §103.2 is explicit: "11
/// файлов под теми же именами").
/// </summary>
internal static class LegalSourceSet
{
    public const string ChannelOfferFileName = "09-channel-offer.html";
    public const string AppendixBodyMarker = "<!-- APPENDIX-BODY-START -->";
    private const string ManifestFileName = "legal.json";

    public sealed record ManifestFileEntry(string Kind, string Type, string File);

    /// <summary>Builds the artifact into <paramref name="outDir"/> (created if missing). Throws
    /// <see cref="InvalidOperationException"/> with a clear message on any structural problem — a marker
    /// missing from the channel-offer draft, a manifest entry pointing at a file that doesn't exist, etc.
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

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, ManifestFileName), manifestBytes);

        foreach (var entry in entries)
        {
            var sourcePath = Path.Combine(sourceDir, entry.File);
            if (!File.Exists(sourcePath))
                throw new InvalidOperationException(
                    $"{(string.IsNullOrEmpty(entry.Type) ? entry.Kind : entry.Type)}: файл '{entry.File}' не найден в {sourceDir}.");

            var content = NormalizeLineEndings(File.ReadAllText(sourcePath));

            if (entry.Type == "TermsOwner")
                content = SpliceChannelOffer(sourceDir, content);

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

    private static string SpliceChannelOffer(string sourceDir, string termsOwnerContent)
    {
        var offerPath = Path.Combine(sourceDir, ChannelOfferFileName);
        if (!File.Exists(offerPath))
            throw new InvalidOperationException(
                $"TermsOwner: приложение № 1 не найдено ({ChannelOfferFileName} отсутствует в {sourceDir}).");

        var offerContent = NormalizeLineEndings(File.ReadAllText(offerPath));
        var markerIndex = offerContent.IndexOf(AppendixBodyMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException(
                $"{ChannelOfferFileName}: не найдена метка '{AppendixBodyMarker}' — склейка D9 в D3 невозможна " +
                "(ARCHITECTURE_CYCLE11.md §103.2).");

        var afterMarker = offerContent[(markerIndex + AppendixBodyMarker.Length)..].TrimStart('\n', '\r');
        var separator = termsOwnerContent.EndsWith('\n') ? "" : "\n";
        return termsOwnerContent + separator + afterMarker;
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

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");
}
