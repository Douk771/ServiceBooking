using System.Text.Json;
using FluentAssertions;
using ServiceBooking.LegalKit;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>
/// ARCHITECTURE_CYCLE17.md §306.2, API_CONTRACT_CYCLE17.md §327 — the C# half of the shared corpus
/// <c>contracts/legal/runtime-value-forms.json</c>. The TS half is
/// <c>frontend/src/utils/legalRuntimeValues.test.ts</c>. Both sides read the SAME file, so a case added
/// to only one side (the C15-1 failure mode) shows up as a failing test here or there, not as a silent
/// pass. This exercises <see cref="RuntimeValueScanner"/> only — the frontend substitution logic isn't
/// reachable from this project (no Node runtime here).
/// </summary>
public class RuntimeValueFormsCorpusTests
{
    [Fact]
    public void Scanner_SeesMarkup_ExactlyAsTheCorpusDeclares()
    {
        var corpus = LoadCorpus();

        foreach (var c in corpus.Cases)
        {
            RuntimeValueScanner.HasAnyAttributeMatch(c.Html).Should().Be(c.ScannerSees,
                because: $"corpus case '{c.Id}' (contracts/legal/runtime-value-forms.json) declares " +
                         $"scannerSees={c.ScannerSees}");
        }
    }

    [Fact]
    public void Scanner_ClassifiesTheDeclaredName_ForEveryCaseWhereScannerSees()
    {
        var corpus = LoadCorpus();

        foreach (var c in corpus.Cases.Where(c => c.ScannerSees))
        {
            c.Name.Should().NotBeNull($"corpus case '{c.Id}' has scannerSees=true, so name must be set");

            // A KNOWN name never shows up in FindUnknownNames' output; an unknown one always does — so
            // this doubles as the "which name did the scanner extract" check without needing a third
            // scanner method.
            var isKnown = RuntimeValueScanner.KnownNames.Contains(c.Name!);
            var flaggedAsUnknown = RuntimeValueScanner.FindUnknownNames(c.Html).Count > 0;
            flaggedAsUnknown.Should().Be(!isKnown,
                because: $"corpus case '{c.Id}': scanner extracts name '{c.Name}', which is " +
                         $"{(isKnown ? "known" : "unknown")} in RuntimeValueScanner.KnownNames");
        }
    }

    [Fact]
    public void Corpus_Invariant_ScannerSeesImpliesFrontSubstitutes()
    {
        // Guards the corpus file itself against a case that would make the invariant vacuous or wrong —
        // this is the assertion ARCHITECTURE_CYCLE17.md §306.2 calls "the inequality that catches
        // C15-1": scannerSees=true with frontSubstitutes=false must never be declared as acceptable.
        var corpus = LoadCorpus();

        foreach (var c in corpus.Cases.Where(c => c.ScannerSees))
        {
            c.FrontSubstitutes.Should().BeTrue(
                $"corpus case '{c.Id}': scannerSees=true must imply frontSubstitutes=true (§306.2 invariant)");
        }
    }

    [Fact]
    public void Scanner_UnknownNameCase_BuildMustFail()
    {
        var corpus = LoadCorpus();
        var unknown = corpus.UnknownNameCase;

        unknown.Should().NotBeNull("contracts/legal/runtime-value-forms.json must declare unknownNameCase");
        RuntimeValueScanner.KnownNames.Should().NotContain(unknown!.Name,
            "unknownNameCase is only meaningful while its declared name stays outside the known set");
        RuntimeValueScanner.FindUnknownNames(unknown.Html).Should().NotBeEmpty(
            "the scanner must flag this markup so the build fails on a typo, per §306.2/§256.5");
    }

    /// <summary>Known-name-only smoke test on the real committed document (US-17-07's second criterion):
    /// no un-substitutable data-legal-value markup should remain in the canonical file after a real
    /// substitution pass — but that pass lives in the frontend, so here we only assert the scanner finds
    /// no UNKNOWN names in it (a proxy this project can check without Node).</summary>
    [Fact]
    public void RealBookingNoticeDocument_HasNoUnknownRuntimeValueNames()
    {
        var path = FindRepoFile(Path.Combine("ServiceBooking.API", "App_Data", "legal", "05-booking-notice.html"));
        var html = File.ReadAllText(path);
        RuntimeValueScanner.FindUnknownNames(html).Should().BeEmpty(
            "05-booking-notice.html is the canonical example referenced by ARCHITECTURE_CYCLE17.md §306.2 " +
            "and must only use known runtime-value names");
    }

    private static RuntimeValueFormsCorpus LoadCorpus()
    {
        var path = FindRepoFile(Path.Combine("contracts", "legal", "runtime-value-forms.json"));
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<RuntimeValueFormsCorpus>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        doc.Should().NotBeNull("contracts/legal/runtime-value-forms.json must parse as JSON");
        return doc!;
    }

    /// <summary>Same "walk up to ServiceBooking.sln" convention as
    /// <c>CommittedLegalArtifactTests.FindCommittedLegalArtifactDir</c> — duplicated (not shared) per
    /// that file's own note that the two callers don't share a build step.</summary>
    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "ServiceBooking.sln")))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' by walking up from the test output directory to the " +
            "repository root (marked by 'ServiceBooking.sln').");
    }

    private sealed class RuntimeValueFormsCorpus
    {
        public List<CorpusCase> Cases { get; set; } = [];
        public UnknownCase? UnknownNameCase { get; set; }
    }

    private sealed class CorpusCase
    {
        public string Id { get; set; } = "";
        public string Html { get; set; } = "";
        public string? Name { get; set; }
        public bool ScannerSees { get; set; }
        public bool FrontSubstitutes { get; set; }
    }

    private sealed class UnknownCase
    {
        public string Id { get; set; } = "";
        public string Html { get; set; } = "";
        public string Name { get; set; } = "";
        public bool ScannerSees { get; set; }
        public bool BuildMustFail { get; set; }
    }
}
