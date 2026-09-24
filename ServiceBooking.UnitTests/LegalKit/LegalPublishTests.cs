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

    // The two Roskomnadzor-registry keys are optional (ARCHITECTURE_CYCLE11.md §103.3): publish must
    // succeed either with or without them, and — crucially — must never leave a half-filled sentence
    // behind when they are absent. See LegalKitFixture.PrivacyHtmlWithRknBullet for the exact real
    // sentence this guards (legal-drafts/01-privacy-policy.html line 24).

    [Fact]
    public void Publish_RknValuesPresent_KeepsTheRegistryBulletSubstituted()
    {
        var source = LegalKitFixture.CreateSourceDir(privacyHtml: LegalKitFixture.PrivacyHtmlWithRknBullet);
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJson());
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(0);
            var privacyHtml = File.ReadAllText(Path.Combine(outDir, "privacy.2026-10-05.html"));
            privacyHtml.Should().Contain("регистрационный номер 00-00-000000");
            privacyHtml.Should().Contain("уведомление направлено 01.01.2026");
            privacyHtml.Should().Contain("bullet before").And.Contain("bullet after");
            privacyHtml.Should().NotContain("{{");
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
    public void Publish_RknValuesAbsent_DropsTheWholeRegistryBulletAndKeepsNeighborsIntact()
    {
        var source = LegalKitFixture.CreateSourceDir(privacyHtml: LegalKitFixture.PrivacyHtmlWithRknBullet);
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJsonWithoutRknKeys());
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(0, "publishing without the registry number is legal (152-ФЗ doesn't require it) and must not be blocked");
            var privacyHtml = File.ReadAllText(Path.Combine(outDir, "privacy.2026-10-05.html"));
            privacyHtml.Should().NotContain("регистрационный номер");
            privacyHtml.Should().NotContain("НОМЕР_УВЕДОМЛЕНИЯ_РКН");
            privacyHtml.Should().NotContain("ДАТА_УВЕДОМЛЕНИЯ_РКН");
            privacyHtml.Should().NotContain("{{");
            privacyHtml.Should().Contain("bullet before").And.Contain("bullet after");
            // The remaining <li>s stay well-formed: exactly two <li>...</li> pairs left in the list.
            System.Text.RegularExpressions.Regex.Matches(privacyHtml, "<li>").Count.Should().Be(2);
            System.Text.RegularExpressions.Regex.Matches(privacyHtml, "</li>").Count.Should().Be(2);
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    /// <summary>Docstring on <c>PublishCommand.BlockElementRegex</c> claims the removal only ever touches
    /// the nearest &lt;li&gt;/&lt;p&gt; — a missing optional placeholder sitting in bare text (a &lt;div&gt;,
    /// or no wrapping element at all) is therefore left untouched by removal and must still be caught by
    /// the ordinary leftover scan, blocking publication rather than silently surviving unfilled.</summary>
    [Fact]
    public void Publish_MissingOptionalPlaceholder_OutsideLiOrP_IsNotRemoved_AndBlocksPublication()
    {
        var source = LegalKitFixture.CreateSourceDir(
            privacyHtml: "<div>регистрационный номер {{НОМЕР_УВЕДОМЛЕНИЯ_РКН}}</div>");
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJsonWithoutRknKeys());
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(5, "a missing optional placeholder outside <li>/<p> is not removed, so it must still block publication");
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

    /// <summary>Review finding 5.1: a block dropped for a missing optional key must not silently take an
    /// unrelated REQUIRED placeholder down with it — publish must refuse instead of guessing.</summary>
    [Fact]
    public void Publish_BlockRemovedForMissingOptionalKey_AlsoContainsARequiredPlaceholder_IsRejected()
    {
        var source = LegalKitFixture.CreateSourceDir(
            privacyHtml: "<ul>\n<li>bullet before</li>\n" +
                         "<li>ИНН {{ИНН_ОПЕРАТОРА}}; сведения внесены в реестр: {{НОМЕР_УВЕДОМЛЕНИЯ_РКН}}</li>\n" +
                         "<li>bullet after</li>\n</ul>");
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var valuesPath = WriteValuesFile(LegalKitFixture.ValidValuesJsonWithoutRknKeys());
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(5, "removing the block would silently drop ИНН_ОПЕРАТОРА, a required requisite that has a value");
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
    public void Publish_OnlyOneOfTwoRknValuesPresent_StillDropsTheWholeBulletRatherThanHalfSubstitutingIt()
    {
        var source = LegalKitFixture.CreateSourceDir(privacyHtml: LegalKitFixture.PrivacyHtmlWithRknBullet);
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        // Has НОМЕР_УВЕДОМЛЕНИЯ_РКН but not ДАТА_УВЕДОМЛЕНИЯ_РКН — an in-between state a real operator
        // could plausibly have if the two facts arrive on different days.
        var json = LegalKitFixture.ValidValuesJsonWithoutRknKeys()
            .Replace("\"НДС_ОГОВОРКА\"", "\"НОМЕР_УВЕДОМЛЕНИЯ_РКН\": \"00-00-000000\",\n            \"НДС_ОГОВОРКА\"");
        var valuesPath = WriteValuesFile(json);
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(0);
            var privacyHtml = File.ReadAllText(Path.Combine(outDir, "privacy.2026-10-05.html"));
            privacyHtml.Should().NotContain("регистрационный номер", "a half-filled sentence is worse than no sentence");
            privacyHtml.Should().NotContain("00-00-000000");
            privacyHtml.Should().NotContain("{{");
            privacyHtml.Should().Contain("bullet before").And.Contain("bullet after");
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
