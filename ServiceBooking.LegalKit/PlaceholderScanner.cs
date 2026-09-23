using System.Text.RegularExpressions;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.LegalKit;

/// <summary>
/// The CLI's placeholder detection. ARCHITECTURE_CYCLE11.md §106.3 requires exactly one regex for "is
/// this an unresolved placeholder" across the whole repository — <see cref="KnownPattern"/> below IS
/// <see cref="LegalDocumentProvider.PlaceholderPattern"/>, not a second declaration of the same string.
///
/// R10 / risk A4 (API_CONTRACT_CYCLE11.md §112): that one regex only recognizes uppercase Cyrillic
/// (<c>{{[А-ЯЁ_]+}}</c>) by design — it is the exact token <see cref="LegalDocumentProvider"/> itself
/// treats as "still a hole" when deciding whether a non-draft document may be served. A placeholder
/// typo'd in Latin (<c>{{OPERATOR_NAME}}</c>) or lowercase Cyrillic never matches it, so it reads as
/// "already filled in" everywhere that regex is the only check — which is exactly the class of bug this
/// scanner's second method, <see cref="FindUnknownForms"/>, exists to catch. It is deliberately NOT
/// folded into <see cref="KnownPattern"/> itself: broadening the shared regex would change what
/// <see cref="LegalDocumentProvider"/> treats as a publish-blocking hole in the running product (a
/// behavior change to the product's read path that ARCHITECTURE_CYCLE11.md §104.2 explicitly rules out —
/// "ни одного изменения в поведении рантайма"). Instead:
///  - `legal check` calls <see cref="FindUnknownForms"/> against the raw draft sources and fails the
///    build the moment an unrecognized `{{…}}` token shows up (API_CONTRACT_CYCLE11.md §112, risk A4)
///    instead of silently treating it as filled text.
///  - `legal publish` scans the FINAL, substituted HTML with the same broad token pattern (not just
///    <see cref="KnownPattern"/>) before writing anything to disk — a Latin/lowercase placeholder that
///    was never a real substitution key still shows up as "a `{{…}}` survived to the output" and blocks
///    publication under ARCHITECTURE_CYCLE11.md §105.3 step 2, regardless of what script or case it used.
/// </summary>
internal static partial class PlaceholderScanner
{
    /// <summary>The one shared "is this an unresolved placeholder" pattern (§106.3) — delegates to
    /// <see cref="LegalDocumentProvider.PlaceholderPattern"/> rather than declaring its own copy.</summary>
    public static string KnownPattern => LegalDocumentProvider.PlaceholderPattern;

    /// <summary>The 13 values-file keys plus the 2 manifest-derived ones (version/effective date) —
    /// contracts/cycle11/legal-values.schema.json's "15 имён" (API_CONTRACT_CYCLE11.md §112, risk A4).
    /// Any `{{…}}` token in a source file whose inner text isn't exactly one of these (in this exact
    /// case) is an unrecognized form of placeholder, not a filled-in value.</summary>
    public static readonly IReadOnlySet<string> KnownNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "НАИМЕНОВАНИЕ_ОПЕРАТОРА", "ИНН_ОПЕРАТОРА", "ОГРН_ОПЕРАТОРА", "ЮРИДИЧЕСКИЙ_АДРЕС",
        "ПОЧТОВЫЙ_АДРЕС", "ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ", "ТЕЛЕФОН_ОПЕРАТОРА", "ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ",
        "ПОЧТА_ОТВЕТСТВЕННОГО", "НОМЕР_УВЕДОМЛЕНИЯ_РКН", "ДАТА_УВЕДОМЛЕНИЯ_РКН", "СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ",
        "НДС_ОГОВОРКА", "ВЕРСИЯ_ДОКУМЕНТА", "ДАТА_ВСТУПЛЕНИЯ_В_СИЛУ",
    };

    /// <summary>Matches ANY <c>{{...}}</c> token regardless of script/case — the superset
    /// <see cref="KnownPattern"/> is a narrow subset of. Used only for the two checks documented on the
    /// class, never as a substitute for <see cref="KnownPattern"/> in a context that mirrors product
    /// behavior.</summary>
    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex BroadTokenRegex();

    /// <summary>Every <c>{{…}}</c> token in <paramref name="html"/> that is NOT an exact, case-sensitive
    /// match of one of the 15 <see cref="KnownNames"/> — i.e. a placeholder written in a form the
    /// product's own detector would silently accept as "filled in".</summary>
    public static IReadOnlyList<string> FindUnknownForms(string html)
    {
        var found = new List<string>();
        foreach (Match m in BroadTokenRegex().Matches(html))
        {
            var inner = m.Value[2..^2];
            if (!KnownNames.Contains(inner))
                found.Add(m.Value);
        }
        return found;
    }

    /// <summary>True if any <c>{{…}}</c> token — known or unknown form — remains in <paramref name="html"/>.
    /// Used by `legal publish`'s post-substitution gate (ARCHITECTURE_CYCLE11.md §105.3 step 2), which
    /// must not let ANY leftover token through, not only the Cyrillic-uppercase ones.</summary>
    public static IReadOnlyList<string> FindAllTokens(string html) =>
        BroadTokenRegex().Matches(html).Select(m => m.Value).ToList();
}
