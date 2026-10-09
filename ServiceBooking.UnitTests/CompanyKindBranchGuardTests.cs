using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.3.2, ARCHITECTURE_CYCLE42.md §42.3.4 — with four kinds of company a binary branch on the kind ("Orders ? shop : salon") silently sends the
/// other kinds down the wrong path. A new branch must be a <c>switch</c> over all kinds (with an `UnreachableException` default) or a <c>CompanyKindTraits</c> property.
/// This scans the committed source and fails on a ternary on <c>CompanyKind.Orders</c>, <c>.Stays</c> or <c>.Baths</c> outside the allow-list.
/// </summary>
public class CompanyKindBranchGuardTests
{
    private static readonly Regex Ternary = new(@"CompanyKind\.(Orders|Stays|Baths)\s*\?", RegexOptions.Compiled);

    /// <summary>The regex itself is the guard: these lines must be caught, a nullable type or a plain comparison must not.</summary>
    public static IEnumerable<object[]> Caught() =>
    [
        ["var x = kind == CompanyKind.Orders ? a : b;"],
        ["var x = kind == CompanyKind.Stays ? a : b;"],
        ["var x = company.Kind == CompanyKind.Baths ? a : b;"],
        ["var x = k is CompanyKind.Baths ?a : b;"],
    ];

    [Theory, MemberData(nameof(Caught))]
    public void Regex_catches_a_ternary_on_orders_stays_and_baths(string line) => Ternary.IsMatch(line).Should().BeTrue();

    [Theory]
    [InlineData("CompanyKind? kind = null;")]
    [InlineData("if (kind == CompanyKind.Baths) return;")]
    [InlineData("CompanyKind.Baths => x,")]
    public void Regex_ignores_nullable_types_and_plain_comparisons(string line) => Ternary.IsMatch(line).Should().BeFalse();

    private static readonly string[] AllowList =
    [
        "CompanyKindQuery.cs",
    ];

    /// <summary>
    /// Known ternary still to be rewritten by the billing task (BE-42-P, ARCHITECTURE_CYCLE42.md §42.3.4: AdminBillingController 532…586). One exact line, not a whole file:
    /// delete this entry together with the rewrite.
    /// </summary>
    private static readonly (string File, string Fragment)[] PendingLines =
    [
        ("AdminBillingController.cs", "kind == CompanyKind.Stays ? OrdersPlan.FallbackFree"),
    ];

    [Fact]
    public void No_new_binary_branch_on_the_company_kind_outside_the_allow_list()
    {
        var root = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(root, "ServiceBooking.API")); i++) root = Path.GetDirectoryName(root)!;
        var violations = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "ServiceBooking.API"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (AllowList.Any(a => file.EndsWith(a, StringComparison.Ordinal))) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Ternary.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//")
                    && !PendingLines.Any(p => file.EndsWith(p.File, StringComparison.Ordinal) && lines[i].Contains(p.Fragment, StringComparison.Ordinal)))
                    violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
        }
        violations.Should().BeEmpty("a ternary on CompanyKind.Orders sends a «Дома» company down the shop path; use CompanyKindTraits or a switch over all kinds (ARCHITECTURE_CYCLE42.md §42.3.4)");
    }
}
