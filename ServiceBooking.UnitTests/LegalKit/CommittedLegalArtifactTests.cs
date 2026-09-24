using FluentAssertions;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>
/// Cycle 13 post-merge review finding: `ServiceBooking.API/App_Data/legal/13-public-address-notice.html`
/// was copied into the committed artifact directly, bypassing `ServiceBooking.LegalKit build`, and kept
/// internal `ЮРИСТУ:`/`РАЗРАБОТКЕ:` review comments that `LegalSourceSet.Build` strips (see
/// <see cref="LegalBuildDeterminismTests.Build_StripsHtmlComments_FromArtifact_ButStillSplicesUsingTheMarkerComment"/>).
/// That stripping is exercised only against the in-memory/temp-directory output of a fresh build, never
/// against the file actually committed to <c>ServiceBooking.API/App_Data/legal</c> — so a hand-copied file
/// that skips the build step slips past every existing test. This test reads the committed artifact
/// directly off disk (no host, no DB, no HTTP — a plain filesystem read of static content) so a future
/// hand-edit or hand-copy into the artifact directory is caught immediately.
/// </summary>
public class CommittedLegalArtifactTests
{
    [Fact]
    public void CommittedLegalArtifact_NoHtmlFileContainsAnHtmlComment()
    {
        var artifactDir = FindCommittedLegalArtifactDir();
        var htmlFiles = Directory.GetFiles(artifactDir, "*.html");

        htmlFiles.Should().NotBeEmpty("the committed legal artifact directory must exist and contain files");

        foreach (var file in htmlFiles)
        {
            var content = File.ReadAllText(file);
            content.Should().NotContain(
                "<!--",
                because: $"'{Path.GetFileName(file)}' is committed application content served verbatim by " +
                          "GET /api/legal/texts/{key} (or a published legal document) — any HTML comment " +
                          "left in it (e.g. internal 'ЮРИСТУ:'/'РАЗРАБОТКЕ:' review notes) ships straight " +
                          "to the browser and is visible in page source/devtools. Only 'legal build' output, " +
                          "which strips comments, may land here — never a hand copy.");
        }
    }

    /// <summary>Walks up from the test assembly's output directory to the repository root, then down into
    /// the known committed artifact path. Avoids any dependency on the API project being loaded/hosted.</summary>
    private static string FindCommittedLegalArtifactDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "ServiceBooking.API", "App_Data", "legal");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "ServiceBooking.sln")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate 'ServiceBooking.API/App_Data/legal' by walking up from the test output " +
            "directory to the repository root (marked by 'ServiceBooking.sln').");
    }
}
