using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Showcase.Tariffs;

/// <summary>
/// The list of phrases a public tariff text must not contain (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.9.2): "от 3 900 ₽", "после заявки", "мы свяжемся с вами" and so on.
/// ONE list for C# and TypeScript — the embedded <c>contracts/cycle48/forbidden-phrases.json</c>; a phrase is added there, not here.
/// Used by <c>ops tariffs plan|apply</c> (a warning when an administrator edited a field and the forbidden wording is still there) and by tests.
/// </summary>
public static class TariffForbiddenPhrases
{
    public const string ResourceName = "forbidden-phrases.json";
    public const string TariffTextsScope = "tariffTexts";

    private sealed record Pattern(string Id, Regex Regex);

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Pattern>>> ByScope = new(Load);

    /// <summary>The first forbidden match of <paramref name="text"/> in the given scope, or <c>null</c> when the text is clean.</summary>
    public static string? FindFirst(string? text, string scope = TariffTextsScope)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (!ByScope.Value.TryGetValue(scope, out var patterns)) return null;
        foreach (var pattern in patterns)
        {
            var match = pattern.Regex.Match(text);
            if (match.Success) return match.Value;
        }

        return null;
    }

    /// <summary>Ids of the patterns of a scope (tests assert that the list is not empty, i.e. that a typo in a scope name cannot silence the check).</summary>
    public static IReadOnlyList<string> PatternIds(string scope = TariffTextsScope) =>
        ByScope.Value.TryGetValue(scope, out var patterns) ? patterns.Select(p => p.Id).ToList() : [];

    private static IReadOnlyDictionary<string, IReadOnlyList<Pattern>> Load()
    {
        using var stream = typeof(TariffForbiddenPhrases).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing — check the EmbeddedResource item in ServiceBooking.API.csproj.");
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, List<Pattern>>(StringComparer.Ordinal);
        foreach (var item in doc.RootElement.GetProperty("patterns").EnumerateArray())
        {
            var options = RegexOptions.CultureInvariant;
            if (item.GetProperty("ignoreCase").GetBoolean()) options |= RegexOptions.IgnoreCase;
            var pattern = new Pattern(item.GetProperty("id").GetString()!, new Regex(item.GetProperty("regex").GetString()!, options, TimeSpan.FromSeconds(1)));
            foreach (var scope in item.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()!))
            {
                if (!result.TryGetValue(scope, out var list)) result[scope] = list = [];
                list.Add(pattern);
            }
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Pattern>)kv.Value, StringComparer.Ordinal);
    }
}
