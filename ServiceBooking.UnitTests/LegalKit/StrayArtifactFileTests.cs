using FluentAssertions;
using ServiceBooking.LegalKit;
using ServiceBooking.LegalKit.Commands;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>
/// Coordinator-reported defect (T8 real run): `legal build` left behind cycle 5's orphaned stub files
/// (`privacy.html`, `terms.html`, …) instead of removing them, and `legal check` didn't notice because it
/// only compares files the manifest still references. ARCHITECTURE_CYCLE11.md §103.2 draws the artifact
/// as an EXACT set of files, not a floor — both gaps closed here.
/// </summary>
public class StrayArtifactFileTests
{
    [Fact]
    public void Build_OutDirHasOrphanedHtmlFromAPriorManifest_RemovesIt()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var orphanPath = Path.Combine(outDir, "orphan-from-a-previous-manifest.html");
        File.WriteAllText(orphanPath, "<p>stale</p>");
        try
        {
            var exitCode = BuildCommand.Run(CliArgs.Parse(["--source", source, "--out", outDir]));

            exitCode.Should().Be(0);
            File.Exists(orphanPath).Should().BeFalse("build must remove artifact files it no longer produces");
            File.Exists(Path.Combine(outDir, "privacy.html")).Should().BeTrue("files the manifest DOES still reference must survive");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Build_NonHtmlStrayFile_IsLeftAlone()
    {
        // Scoped to *.html only — a README, a .gitkeep, or anything else that happens to live in the
        // artifact directory is not "artifact surface" and must never be deleted by build.
        var source = LegalKitFixture.CreateSourceDir();
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var keepPath = Path.Combine(outDir, ".gitkeep");
        File.WriteAllText(keepPath, "");
        try
        {
            BuildCommand.Run(CliArgs.Parse(["--source", source, "--out", outDir])).Should().Be(0);
            File.Exists(keepPath).Should().BeTrue();
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Build_DryRun_DoesNotDeleteOrphanedFile()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var orphanPath = Path.Combine(outDir, "orphan.html");
        File.WriteAllText(orphanPath, "<p>stale</p>");
        try
        {
            BuildCommand.Run(CliArgs.Parse(["--source", source, "--out", outDir, "--dry-run"])).Should().Be(0);
            File.Exists(orphanPath).Should().BeTrue("--dry-run must not touch the filesystem");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Check_RootHasOrphanedFileNotProducedBySource_FailsWithArtifactDriftCode()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var root = Path.Combine(Path.GetTempPath(), "legalkit-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, root);
            // Simulate exactly the coordinator's repro: a stub the manifest no longer references.
            File.WriteAllText(Path.Combine(root, "orphan-from-a-previous-manifest.html"), "<p>stale</p>");

            var exitCode = CheckCommand.Run(CliArgs.Parse(["--source", source, "--root", root]));

            exitCode.Should().Be(3);
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(root);
        }
    }

    [Fact]
    public void Check_CleanlyBuiltRoot_Passes()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var root = Path.Combine(Path.GetTempPath(), "legalkit-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, root);

            CheckCommand.Run(CliArgs.Parse(["--source", source, "--root", root])).Should().Be(0);
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(root);
        }
    }

    [Fact]
    public void FindStrayHtmlFiles_FileNotInManifest_IsReported()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var dir = Path.Combine(Path.GetTempPath(), "legalkit-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "not-in-manifest.html"), "");
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "");
        try
        {
            var stray = LegalSourceSet.FindStrayHtmlFiles(dir, source);

            stray.Should().ContainSingle().Which.Should().Be("not-in-manifest.html");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(dir);
        }
    }
}
