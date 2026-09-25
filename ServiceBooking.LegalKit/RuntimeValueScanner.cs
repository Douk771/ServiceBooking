using System.Text.RegularExpressions;

namespace ServiceBooking.LegalKit;

/// <summary>
/// ARCHITECTURE_CYCLE15.md §256.5 — the CLI's detector for the SHOW-TIME substitution markup
/// (<c>data-legal-value|when|unless="…"</c>, §256.2), a deliberately different mechanism from
/// <see cref="PlaceholderScanner"/>'s <c>{{…}}</c> PUBLISH-TIME placeholders (§256.1: the two syntaxes
/// exist precisely so runtime values never trip <see cref="LegalDocumentProvider.PlaceholderPattern"/>
/// and block publication). This scanner does NOT touch <see cref="PlaceholderScanner"/> or
/// <see cref="LegalDocumentProvider.PlaceholderPattern"/> in any way — no change to product runtime
/// behavior (ARCHITECTURE_CYCLE11.md §104.2's rule, carried forward).
///
/// The known-name list is declared ONCE here (<see cref="KnownNames"/>) and mirrored on the frontend by
/// <c>frontend/src/utils/legalRuntimeValues.ts</c>'s <c>LEGAL_RUNTIME_VALUE_NAMES</c> — kept in sync by
/// a grep-based test (§262 T-A4), not by a shared file (the two sides don't share a build step).
/// </summary>
internal static partial class RuntimeValueScanner
{
    /// <summary>The one name this cycle introduces (§256.3). Adding a second runtime value later is a
    /// one-line change here plus a matching one-line change to LEGAL_RUNTIME_VALUE_NAMES.</summary>
    public static readonly IReadOnlySet<string> KnownNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "companyName",
    };

    [GeneratedRegex(@"data-legal-(value|when|unless)\s*=\s*""([^""]*)""")]
    private static partial Regex AttributeRegex();

    /// <summary>Test-only probe (ARCHITECTURE_CYCLE17.md §306.2 corpus tests, `InternalsVisibleTo`
    /// above): true iff <paramref name="html"/> contains at least one <c>data-legal-value|when|unless</c>
    /// attribute the scanner's own regex recognizes — independent of whether its name is known. This is
    /// the scanner-side half of the shared corpus's "scannerSees" flag.</summary>
    internal static bool HasAnyAttributeMatch(string html) => AttributeRegex().IsMatch(html);

    /// <summary>
    /// Every <c>data-legal-value|when|unless="…"</c> attribute in <paramref name="html"/> whose name
    /// isn't an exact match of one of <see cref="KnownNames"/> — catches a typo like
    /// <c>data-legal-value="companyname"</c> at build time instead of shipping a span that will always
    /// render empty (§256.5's own promise: "опечатка... красит сборку, а не молча даёт вечно пустой
    /// span").
    /// </summary>
    public static IReadOnlyList<string> FindUnknownNames(string html)
    {
        var found = new List<string>();
        foreach (Match m in AttributeRegex().Matches(html))
        {
            var name = m.Groups[2].Value;
            if (!KnownNames.Contains(name))
                found.Add(m.Value);
        }
        return found;
    }

    /// <summary>
    /// For every KNOWN name actually used in <paramref name="html"/>: a warning (not a failure) when the
    /// name appears as a bare <c>data-legal-value</c> outside of any <c>data-legal-when</c>/
    /// <c>data-legal-unless</c> pair for that same name — §256.5's documented, deliberately non-fatal
    /// case ("текст станет грамматически неполным, если значение неизвестно").
    /// </summary>
    public static IReadOnlyList<string> FindValuesWithoutFallback(string html)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var whenOrUnless = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in AttributeRegex().Matches(html))
        {
            var kind = m.Groups[1].Value;
            var name = m.Groups[2].Value;
            if (!KnownNames.Contains(name)) continue;

            if (kind == "value") names.Add(name);
            else whenOrUnless.Add(name);
        }

        return names.Where(n => !whenOrUnless.Contains(n)).ToList();
    }
}
