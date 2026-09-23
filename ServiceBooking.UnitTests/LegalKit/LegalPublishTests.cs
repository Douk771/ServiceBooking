using FluentAssertions;
using ServiceBooking.LegalKit;
using ServiceBooking.LegalKit.Commands;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>`legal publish`/`legal rollback` (ARCHITECTURE_CYCLE11.md §105.3) — every case here works
/// against temp directories only, no database, no HTTP, no server: publish deliberately never opens a
/// database connection (§106 invariant 1), so this IS the right layer to test it at.</summary>
public class LegalPublishTests
{
    private static string WriteValuesFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "legal-values-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Publish_ValidInputs_WritesVersionedFilesAndFlipsIsDraftFalse()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(0);
            File.Exists(Path.Combine(outDir, "privacy.2026-10-05.html")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "legal.json")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "legal.published.json")).Should().BeTrue();

            var published = LegalProviderFactory.CreateForRoot(outDir).LoadStrict();
            published.Documents.Values.Should().OnlyContain(d => !d.IsDraft);
            published.Documents.Values.Should().OnlyContain(d => d.Version == "2026-10-05");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    [Fact]
    public void Publish_VersionWithDraftSuffix_IsRejectedBeforeWritingAnything()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05-draft", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(5);
            Directory.Exists(outDir).Should().BeFalse("publish must refuse before a single byte is written (§105.3 step 2)");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    [Fact]
    public void Publish_MissingValuesFile_IsRejectedBeforeWritingAnything()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", Path.Combine(Path.GetTempPath(), "no-such-file.json"),
                 "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(5);
            Directory.Exists(outDir).Should().BeFalse();
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Publish_LeftoverUnknownFormPlaceholder_IsRejected()
    {
        // Risk A4: a Latin placeholder in the source must block publish even though it never matches a
        // substitution key and would otherwise sail straight through as "already filled in".
        var source = LegalKitFixture.CreateSourceDir();
        File.WriteAllText(Path.Combine(source, "ad.html"), "<p>{{OPERATOR_NAME}}</p>");
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(5);
            Directory.Exists(outDir).Should().BeFalse();
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    [Fact]
    public void Rollback_AfterOnePublish_RestoresDraftManifest()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);
            // Rollback restores whatever manifest was current immediately BEFORE the most recent
            // publish (§105.3) — a first-ever publish has no such manifest (asserted separately below),
            // so this test publishes twice and rolls back the second.
            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]))
                .Should().Be(0);
            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-11-01", "--effective-from", "2026-11-01"]))
                .Should().Be(0);

            var exitCode = RollbackCommand.Run(CliArgs.Parse(["--out", outDir]));

            exitCode.Should().Be(0);
            var manifestAfterRollback = File.ReadAllText(Path.Combine(outDir, "legal.json"));
            manifestAfterRollback.Should().Contain("2026-10-05").And.NotContain("2026-11-01");
            manifestAfterRollback.Should().MatchRegex("\"isDraft\"\\s*:\\s*false");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    [Fact]
    public void Rollback_AfterFirstEverPublish_HasNoPreviousManifestToRestore()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);
            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]))
                .Should().Be(0);

            RollbackCommand.Run(CliArgs.Parse(["--out", outDir])).Should().Be(1);
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    [Fact]
    public void Rollback_NoPublishedLog_ReturnsUsageError()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            RollbackCommand.Run(CliArgs.Parse(["--out", outDir])).Should().Be(1);
        }
        finally
        {
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Rollback_RecordsItsOwnJournalEntry_RatherThanErasingThePublishEntry()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);
            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]))
                .Should().Be(0);
            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-11-01", "--effective-from", "2026-11-01"]))
                .Should().Be(0);

            RollbackCommand.Run(CliArgs.Parse(["--out", outDir])).Should().Be(0);

            var log = File.ReadAllText(Path.Combine(outDir, "legal.published.json"));
            // Both publish entries are still there — rollback appended, it did not delete.
            log.Should().Contain("2026-10-05").And.Contain("2026-11-01").And.Contain("\"rollback\"");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    [Fact]
    public void Rollback_TwiceInARowWithoutANewPublish_RefusesTheSecondCall()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);
            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]))
                .Should().Be(0);
            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-11-01", "--effective-from", "2026-11-01"]))
                .Should().Be(0);

            RollbackCommand.Run(CliArgs.Parse(["--out", outDir])).Should().Be(0);
            var manifestAfterFirstRollback = File.ReadAllText(Path.Combine(outDir, "legal.json"));

            // Second rollback in a row, with no new publish in between: must refuse, not walk one more
            // generation back to the first-ever-publish state.
            RollbackCommand.Run(CliArgs.Parse(["--out", outDir])).Should().Be(1);

            File.ReadAllText(Path.Combine(outDir, "legal.json")).Should().Be(manifestAfterFirstRollback);
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }
}
