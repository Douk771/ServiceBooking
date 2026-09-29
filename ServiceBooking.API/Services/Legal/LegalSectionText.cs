using System.Net;
using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §411 (Т20-08 п. 2) — the C# analog of the frontend's
/// <c>splitLegalSections</c>/<c>findSection</c> (<c>frontend/src/legalSections.ts</c>), for the ONE place
/// the server itself needs to show legal-owned copy rather than hand it whole to the frontend: the
/// <c>guestDataGate.explanation</c> field of <c>GET /api/profile/export</c>. Before this, that field
/// carried the entire uiText file, service commentary ("Служебная справка для команды…") included.
/// </summary>
public static partial class LegalSectionText
{
    [GeneratedRegex(@"<h2[^>]*>(.*?)</h2>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"</(p|li|h[1-6]|div)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockClosingTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLinesRegex();

    /// <summary>Returns the plain-text body of the first <c>&lt;h2&gt;</c> section whose heading
    /// contains <paramref name="headingSubstring"/> (case/whitespace-insensitive, mirroring the
    /// frontend's forgiving match), or <c>null</c> if no such section exists — the caller decides the
    /// fallback (SubjectGateTexts.Fallback for this cycle's one caller), this function never guesses.
    /// Tags are stripped, block-level closing tags become paragraph breaks (<c>\n\n</c>), and HTML
    /// entities are decoded — the result is what a plain-text reader (an exported JSON file) should see,
    /// not the markup meant for a browser.</summary>
    public static string? PlainSection(string html, string headingSubstring)
    {
        var needle = headingSubstring.Trim();
        var matches = HeadingRegex().Matches(html);

        for (var i = 0; i < matches.Count; i++)
        {
            var heading = AnyTagRegex().Replace(matches[i].Groups[1].Value, "").Trim();
            if (!heading.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;

            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : html.Length;
            var sectionHtml = html[start..end];

            return ToPlainText(sectionHtml);
        }

        return null;
    }

    private static string ToPlainText(string html)
    {
        var withBreaks = BlockClosingTagRegex().Replace(html, "\n\n");
        var stripped = AnyTagRegex().Replace(withBreaks, "");
        var decoded = WebUtility.HtmlDecode(stripped);
        var collapsed = ExcessBlankLinesRegex().Replace(decoded, "\n\n");
        return collapsed.Trim();
    }
}
