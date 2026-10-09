using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.4.3 — a number works for every company of its billing account, so the channel-company assignments are
/// neither read nor written any more. The table and the <c>NotificationChannel.Assignments</c> navigation stay (the composite FK and
/// the rollback need them); this scans the committed production source for any reference outside a short allow-list:
/// <list type="bullet">
/// <item><c>CompanyTransferService.cs</c> and <c>AccountDeletionService.cs</c> — they only DELETE legacy rows (the composite FK
/// <c>(ChannelId, BillingAccountId, Transport)</c> would otherwise block moving/removing the company); no add/update there either;</item>
/// <item><c>ShowcaseOwnership.cs</c> — the table-name registry of the showcase purge;</item>
/// <item><c>ApplicationServicesExtensions.cs</c> — a name in a comment (comments are stripped anyway).</item>
/// </list>
/// </summary>
public class AssignmentReadGuardTests
{
    private static readonly Regex Reference = new(@"ChannelCompanyAssignments?\b|\.Assignments\b", RegexOptions.Compiled);
    private static readonly Regex WriteOtherThanDelete = new(@"ChannelCompanyAssignments\s*\.\s*(Add|AddRange|AddAsync|Update|UpdateRange|Attach)\b|ChannelCompanyAssignments[^;]*ExecuteUpdate", RegexOptions.Compiled);
    private static readonly string[] AllowedAnywhere = ["ShowcaseOwnership.cs", "ApplicationServicesExtensions.cs"];
    private static readonly string[] DeleteOnly = ["CompanyTransferService.cs", "AccountDeletionService.cs"];

    private static string FindRoot()
    {
        var root = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(root, "ServiceBooking.API")); i++) root = Path.GetDirectoryName(root)!;
        return root;
    }

    private static IEnumerable<string> ApiSources(string root) =>
        Directory.GetFiles(Path.Combine(root, "ServiceBooking.API"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static string CodeOf(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
    }

    [Fact]
    public void Production_code_does_not_touch_channel_company_assignments_outside_the_allow_list()
    {
        var root = FindRoot();
        var violations = new List<string>();
        foreach (var file in ApiSources(root))
        {
            var name = Path.GetFileName(file);
            if (AllowedAnywhere.Contains(name) || DeleteOnly.Contains(name)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Reference.IsMatch(CodeOf(lines[i])))
                    violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
        }
        violations.Should().BeEmpty("the assignments are not read or written any more: the channels of a company are those of its billing account (ARCHITECTURE_CYCLE40.md §40.4)");
    }

    [Fact]
    public void The_two_allowed_services_only_delete_assignment_rows()
    {
        var root = FindRoot();
        var violations = new List<string>();
        var seen = new HashSet<string>();
        foreach (var file in ApiSources(root).Where(f => DeleteOnly.Contains(Path.GetFileName(f))))
        {
            seen.Add(Path.GetFileName(file));
            var code = string.Join("\n", File.ReadAllLines(file).Select(CodeOf));
            if (WriteOtherThanDelete.IsMatch(code)) violations.Add(Path.GetRelativePath(root, file));
            Regex.IsMatch(code, @"ChannelCompanyAssignments\s*\.\s*Remove").Should().BeTrue($"{Path.GetFileName(file)} is on the list because it deletes the rows");
        }
        seen.Should().BeEquivalentTo(DeleteOnly);
        violations.Should().BeEmpty("the only allowed use of the table is deleting legacy rows (the composite FK)");
    }

    [Fact]
    public void The_guard_itself_finds_a_violation_when_there_is_one()
    {
        Reference.IsMatch(CodeOf("var x = await db.ChannelCompanyAssignments.AsNoTracking().ToListAsync();")).Should().BeTrue();
        Reference.IsMatch(CodeOf("channel.Assignments.Count")).Should().BeTrue();
        Reference.IsMatch(CodeOf("// ChannelCompanyAssignment was here")).Should().BeFalse("a comment is not a use");
        WriteOtherThanDelete.IsMatch("db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment());").Should().BeTrue();
        WriteOtherThanDelete.IsMatch("db.ChannelCompanyAssignments.RemoveRange(rows);").Should().BeFalse();
    }
}
