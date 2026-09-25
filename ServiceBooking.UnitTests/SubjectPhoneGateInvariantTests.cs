using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceBooking.UnitTests;

/// <summary>
/// TD-03's structural guard (ARCHITECTURE_CYCLE16.md §245.5, R1/A3). The gate itself
/// (<c>SubjectScopeResolver</c>) only helps if nobody adds a SIXTH place that matches a row by a raw
/// phone-number string without going through it. This is a plain text scan of committed source — no
/// host, no DB, no HTTP, just <c>File.ReadAllText</c> against files already on disk (same technique as
/// <c>LegalKit.CommittedLegalArtifactTests</c> and the CI step that scans <c>public/sw.js</c>).
///
/// Every match of one of the four tokens below must carry an inline marker on the SAME line (this file's
/// own convention: appending it after the statement reads better than a preceding line for long
/// predicates, and is enforced identically) reading
/// <c>// SUBJECT-PHONE-GATE: &lt;gated|staff-scoped|not-account-scoped&gt; — &lt;reason&gt;</c>.
/// A token with no marker fails the test by file name and line number, with a pointer back to §245.
/// </summary>
public class SubjectPhoneGateInvariantTests
{
    private static readonly Regex TokenPattern = new(
        @"GuestPhone ==|RecipientPhone ==|\.Phone == |CanonicalPhone ==", RegexOptions.Compiled);

    private static readonly Regex MarkerPattern = new(
        @"SUBJECT-PHONE-GATE:\s*(gated|staff-scoped|not-account-scoped)\s*—\s*\S", RegexOptions.Compiled);

    [Fact]
    public void EveryPhoneStringComparison_InApiControllersAndServices_CarriesAGateMarker()
    {
        var root = FindRepoRoot();
        var scanDirs = new[]
        {
            Path.Combine(root, "ServiceBooking.API", "Controllers"),
            Path.Combine(root, "ServiceBooking.API", "Services"),
        };

        var violations = new List<string>();

        foreach (var dir in scanDirs)
        {
            Directory.Exists(dir).Should().BeTrue($"'{dir}' is expected to exist — scan target moved?");

            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (!TokenPattern.IsMatch(line)) continue;

                    // Marker may be on this line or the previous one (§245.5).
                    var hasMarker = MarkerPattern.IsMatch(line) ||
                        (i > 0 && MarkerPattern.IsMatch(lines[i - 1]));

                    if (!hasMarker)
                        violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        violations.Should().BeEmpty(
            "new matching of a row by a raw phone-number string: pass it through SubjectScopeResolver " +
            "and mark the line '// SUBJECT-PHONE-GATE: gated|staff-scoped|not-account-scoped — <reason>', " +
            "see ARCHITECTURE_CYCLE16.md §245. Violations:\n" + string.Join("\n", violations));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ServiceBooking.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (marked by 'ServiceBooking.sln') by walking up from the " +
            "test output directory.");
    }
}
