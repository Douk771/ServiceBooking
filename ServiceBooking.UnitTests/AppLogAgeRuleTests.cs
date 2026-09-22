using FluentAssertions;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Retention.Rules;

namespace ServiceBooking.UnitTests;

/// <summary>T5-B8/B9. No DB, no network — <see cref="AppLogAgeRule"/> only ever touches a temp directory
/// this test creates and cleans up itself, so it qualifies as a unit test despite touching the
/// filesystem.</summary>
public class AppLogAgeRuleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sb-retention-test-" + Guid.NewGuid());

    public AppLogAgeRuleTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static RetentionContext Ctx(RetentionPeriods periods, bool dryRun = true) =>
        new(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc), periods, 500, dryRun);

    [Fact]
    public async Task ApplyAsync_NoDirectoryConfigured_ReportsNA_TouchesNothing()
    {
        var rule = new AppLogAgeRule();

        var outcome = await rule.ApplyAsync(Ctx(new RetentionPeriods { AppLogDirectory = "" }), CancellationToken.None);

        outcome.Scanned.Should().Be(0);
        outcome.Affected.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_DirectoryConfiguredButMissing_ReportsNA()
    {
        var rule = new AppLogAgeRule();
        var periods = new RetentionPeriods { AppLogDirectory = Path.Combine(_dir, "does-not-exist") };

        var outcome = await rule.ApplyAsync(Ctx(periods), CancellationToken.None);

        outcome.Scanned.Should().Be(0);
        outcome.Affected.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_FreshFile_NotCountedAsStale()
    {
        var file = Path.Combine(_dir, "fresh.log");
        await File.WriteAllTextAsync(file, "hello");

        var periods = new RetentionPeriods { AppLogDirectory = _dir, AppLogDays = 90 };
        var rule = new AppLogAgeRule();

        var outcome = await rule.ApplyAsync(Ctx(periods), CancellationToken.None);

        outcome.Scanned.Should().Be(1);
        outcome.Affected.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_OldFile_CountedAsStale_ButNeverDeleted()
    {
        var file = Path.Combine(_dir, "old.log");
        await File.WriteAllTextAsync(file, "hello");
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var periods = new RetentionPeriods { AppLogDirectory = _dir, AppLogDays = 90 };
        var rule = new AppLogAgeRule();

        var outcome = await rule.ApplyAsync(Ctx(periods, dryRun: false), CancellationToken.None);

        outcome.Scanned.Should().Be(1);
        outcome.Affected.Should().Be(1);
        File.Exists(file).Should().BeTrue(); // this rule never deletes — a tripwire only, not a cleanup
    }
}
