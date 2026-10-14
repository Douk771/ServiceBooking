namespace ServiceBooking.API.Services.Slots;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.8.1 — soft filter of alcohol/tobacco in item names. A stem is searched from the START of a word
/// (the char before it is not a letter), case-insensitive, ё = е. The list is mirrored in contracts/cycle42/bani-vectors.json (a test compares).
/// </summary>
public static class RestrictedItemFilter
{
    public static readonly IReadOnlyList<string> Stems = new[]
    {
        "пив", "вин", "водк", "коньяк", "виски", "сидр", "медовух", "шампанск", "алкогол", "кальян", "табак", "сигар", "вейп", "снюс"
    };

    /// <summary>The stems found in the text, in the order of <see cref="Stems"/>, without repeats.</summary>
    public static string[] Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var t = text.ToLowerInvariant().Replace('ё', 'е');
        var found = new List<string>();
        foreach (var stem in Stems)
        {
            var from = 0;
            while (from < t.Length)
            {
                var i = t.IndexOf(stem, from, StringComparison.Ordinal);
                if (i < 0) break;
                if (i == 0 || !char.IsLetter(t[i - 1])) { found.Add(stem); break; }
                from = i + 1;
            }
        }
        return found.ToArray();
    }
}
