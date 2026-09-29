using System.Text;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §390 — a suggested shop address from its name: Cyrillic → Latin (GOST 7.79-2000, system B
/// without diacritics; the soft/hard signs are dropped), everything else that is not [a-z0-9] becomes a hyphen. Pure.
/// This is only a SUGGESTION — the owner may type any address; <see cref="SlugPolicy"/> is the rule.
/// </summary>
public static class SlugTransliterator
{
    private const string FallbackBase = "magazin";

    private static readonly Dictionary<char, string> Map = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "c",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya",
    };

    /// <summary>Transliterates and normalizes a name into an address base (may be shorter than the minimum length — see <see cref="ToBase"/>).</summary>
    public static string Slugify(string? name)
    {
        var sb = new StringBuilder();
        foreach (var raw in (name ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (Map.TryGetValue(raw, out var latin)) sb.Append(latin);
            else if (raw is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(raw);
            else sb.Append('-');
        }
        return CollapseHyphens(sb.ToString());
    }

    /// <summary>
    /// A valid base of an address: <see cref="Slugify"/>, cut to leave room for a suffix, never shorter than the
    /// minimum length and never a reserved word (both get "-shop").
    /// </summary>
    public static string ToBase(string? name, int maxLength, int minLength, IReadOnlySet<string> reserved)
    {
        // Room for the longest suffix of Candidates ("-" + 4 random hex chars) so every candidate fits maxLength.
        var room = maxLength - 5;
        var value = CutAtHyphen(Slugify(name), room);
        if (value.Length == 0) value = FallbackBase;
        if (value.Length < minLength || reserved.Contains(value)) value = CutAtHyphen($"{value}-{FallbackBase}", room);
        return value;
    }

    /// <summary>
    /// The candidates to try in order: the base, then base-2 … base-20, then the base with a random suffix
    /// (<paramref name="randomSuffix"/>, e.g. 4 hex chars). The caller picks the first one not taken — one query for all.
    /// </summary>
    public static IEnumerable<string> Candidates(string baseSlug, Func<string> randomSuffix)
    {
        yield return baseSlug;
        for (var n = 2; n <= 20; n++) yield return $"{baseSlug}-{n}";
        yield return $"{baseSlug}-{randomSuffix()}";
    }

    private static string CollapseHyphens(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (c == '-' && (sb.Length == 0 || sb[^1] == '-')) continue;
            sb.Append(c);
        }
        return sb.ToString().TrimEnd('-');
    }

    private static string CutAtHyphen(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..maxLength].TrimEnd('-');
    }
}
