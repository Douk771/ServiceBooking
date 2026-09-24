namespace ServiceBooking.API.Services.Geo.Yandex;

/// <summary>
/// Builds the one URL this adapter calls, and — critically — the redacted label that is allowed to
/// reach a log line for it (ARCHITECTURE_CYCLE13.md §206, rung 2 of the defence against a leaked
/// <c>apikey</c>; rung 1 is the named HttpClient's category-level log filter in <c>Program.cs</c>). No
/// caller should ever interpolate the request <see cref="Uri"/> itself into a log message — only
/// <see cref="SafeLabel"/>.
/// </summary>
public static class YandexGeocoderUrls
{
    public static (Uri Request, string SafeLabel) Lookup(string apiUrl, string apiKey, string geocode, int maxCandidates)
    {
        var request = Build(apiUrl, apiKey, geocode, maxCandidates);
        return (request, SafeLabel(request));
    }

    private static Uri Build(string apiUrl, string apiKey, string geocode, int maxCandidates)
    {
        var baseUrl = apiUrl.TrimEnd('/');
        var query = string.Join('&',
            $"apikey={Uri.EscapeDataString(apiKey)}",
            "format=json",
            $"geocode={Uri.EscapeDataString(geocode)}",
            $"results={maxCandidates}",
            "lang=ru_RU");
        return new Uri($"{baseUrl}/?{query}");
    }

    /// <summary>The one place a request <see cref="Uri"/> may be turned into text fit for a log — every
    /// query-string parameter EXCEPT <c>apikey</c> is preserved; the key is replaced with a fixed marker
    /// so a leak is immediately recognizable as "redaction happened here", not as a truncated real key.</summary>
    public static string SafeLabel(Uri request)
    {
        var query = request.Query.TrimStart('?');
        var parts = query.Length == 0 ? [] : query.Split('&');
        var redacted = parts.Select(p => p.StartsWith("apikey=", StringComparison.Ordinal) ? "apikey=***" : p);
        return $"{request.GetLeftPart(UriPartial.Path)}?{string.Join('&', redacted)}";
    }
}
