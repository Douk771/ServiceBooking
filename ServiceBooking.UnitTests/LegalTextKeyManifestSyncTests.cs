using System.Text.Json;
using FluentAssertions;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// CY20-U-11 (ARCHITECTURE_CYCLE20.md §417) — <see cref="LegalTextKey.All"/> is a closed set adding a
/// key is meant to be a manifest edit (see that class's own doc comment), so nothing here should ever
/// let the two drift apart: a key added to one without the other either 404s at runtime
/// (<c>GET /api/legal/texts/{key}</c>) or fails <c>LegalDocumentProvider</c>'s fail-fast startup check
/// (a required key missing from <c>legal.json</c>). Reads <c>legal-drafts/legal.json</c> straight off
/// disk — no DB, no HTTP, no server.
/// </summary>
public class LegalTextKeyManifestSyncTests
{
    [Fact]
    public void LegalTextKeyAll_MatchesUiTextKeysInSourceManifest()
    {
        var manifestPath = FindRepoFile(Path.Combine("legal-drafts", "legal.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var manifestKeys = doc.RootElement.GetProperty("uiTexts")
            .EnumerateArray()
            .Select(e => e.GetProperty("key").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        manifestKeys.Should().BeEquivalentTo(LegalTextKey.All,
            "legal-drafts/legal.json's uiTexts keys and LegalTextKey.All must never silently drift apart " +
            "(ARCHITECTURE_CYCLE20.md §417, CY20-U-11) — a key present in only one of them either 404s at " +
            "runtime or fails LegalDocumentProvider's fail-fast startup check");
    }

    /// <summary>Same "walk up to ServiceBooking.sln" convention as
    /// <c>CommittedLegalArtifactTests.FindCommittedLegalArtifactDir</c>.</summary>
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
}
