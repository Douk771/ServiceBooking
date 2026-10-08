using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.3.2 — with three kinds of company a binary branch on the kind ("Orders ? shop : salon") silently sends the third kind down the wrong
/// path. A new branch must be a <c>switch</c> over all three kinds (with an `UnreachableException` default). This scans the committed source and fails on a ternary on
/// <c>CompanyKind.Orders</c> outside the allow-list (files where the binary question is the real one: "is this a shop at all").
/// </summary>
public class CompanyKindBranchGuardTests
{
    private static readonly Regex Ternary = new(@"CompanyKind\.Orders\s*\?|kind\s*==\s*CompanyKind\.Orders\s*\?", RegexOptions.Compiled);

    private static readonly string[] AllowList =
    [
        "CompanyKindQuery.cs",
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
                if (Ternary.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//"))
                    violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
        }
        violations.Should().BeEmpty("a ternary on CompanyKind.Orders sends a «Дома» company down the shop path; write a switch over all three kinds (ARCHITECTURE_CYCLE37.md §37.3.2)");
    }
}
