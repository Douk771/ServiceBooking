using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.3.4, §40.0 A2 — what is paid is decided per transport from the account's option rows, never from a tariff
/// flag or a paid-numbers count. <c>SubscriptionPlanConfig.AllowNotificationChannel</c> stays as a column/property (a drop is a separate
/// cycle) but may not be READ: this scans the committed source for member access to it (only the seeder's property initializer is allowed)
/// and for the removed <c>PaidNotificationNumbers</c> / <c>ChannelEligibility</c> / <c>ChannelFunding.Rank</c> / <c>TrialMailingRulePolicy</c>.
/// </summary>
public class AllowNotificationChannelReadGuardTests
{
    private static readonly (string Name, Regex Pattern)[] Forbidden =
    [
        ("member access .AllowNotificationChannel", new Regex(@"\.AllowNotificationChannel\b", RegexOptions.Compiled)),
        ("PaidNotificationNumbers", new Regex(@"\bPaidNotificationNumbers\b", RegexOptions.Compiled)),
        ("ChannelEligibility", new Regex(@"\bChannelEligibility\b", RegexOptions.Compiled)),
        ("ChannelFunding.Rank", new Regex(@"\bChannelFunding\.Rank\b", RegexOptions.Compiled)),
        ("TrialMailingRulePolicy", new Regex(@"\bTrialMailingRulePolicy\b", RegexOptions.Compiled)),
        ("SubscriptionResolver.PaidNumbers / IsOptionCurrentlyPaid", new Regex(@"\b(PaidNumbers|IsOptionCurrentlyPaid)\s*\(", RegexOptions.Compiled)),
    ];

    private static string FindRoot()
    {
        var root = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(root, "ServiceBooking.API")); i++) root = Path.GetDirectoryName(root)!;
        return root;
    }

    private static IEnumerable<string> ProductionSources(string root) =>
        new[] { "ServiceBooking.API", "ServiceBooking.Core", "ServiceBooking.Infrastructure" }
            .SelectMany(project => Directory.GetFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static string CodeOf(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
    }

    [Fact]
    public void Production_code_reads_no_tariff_flag_and_no_paid_numbers_count()
    {
        var root = FindRoot();
        var violations = new List<string>();
        foreach (var file in ProductionSources(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = CodeOf(lines[i]);
                foreach (var (name, pattern) in Forbidden)
                    if (pattern.IsMatch(code))
                        violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1} [{name}]: {lines[i].Trim()}");
            }
        }
        violations.Should().BeEmpty("payment is decided per transport by AccountMessagingReader / ChannelOptionFunding, not by a tariff flag (ARCHITECTURE_CYCLE40.md §40.3.4)");
    }

    [Fact]
    public void The_guard_itself_finds_a_violation_when_there_is_one()
    {
        Forbidden[0].Pattern.IsMatch("if (plan.AllowNotificationChannel) return;").Should().BeTrue();
        Forbidden[0].Pattern.IsMatch("AllowNotificationChannel = false,").Should().BeFalse("a property initializer (the seeder) is not a read");
        Forbidden[1].Pattern.IsMatch("plan.PaidNotificationNumbers == 0").Should().BeTrue();
    }
}
