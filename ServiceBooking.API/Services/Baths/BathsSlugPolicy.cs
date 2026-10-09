using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.Services.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.10.1 — address policy of a "Бани" company and of its resource, from the embedded contracts/cycle42/bani-routes.json.
/// Company slugs share the platform-wide space; <c>primer-</c> is reserved for showcase shops.
/// </summary>
public static class BathsSlugPolicy
{
    public const string ResourceName = "bani-routes.json";

    private sealed record Policy(Regex Pattern, int Min, int Max, Regex ResourcePattern, int ResourceMin, int ResourceMax,
        IReadOnlySet<string> Reserved, IReadOnlySet<string> ReservedResource, IReadOnlyList<string> SpaRoutes);

    private static readonly Lazy<Policy> Loaded = new(Load);

    public static IReadOnlySet<string> ReservedSlugs => Loaded.Value.Reserved;
    public static IReadOnlySet<string> ReservedResourceSlugs => Loaded.Value.ReservedResource;
    public static IReadOnlyList<string> SpaRoutes => Loaded.Value.SpaRoutes;

    public static string Normalize(string? slug) => SlugPolicy.Normalize(slug);

    public static SlugCheck Validate(string? slug)
    {
        var p = Loaded.Value;
        var v = Normalize(slug);
        if (v.Length < p.Min || v.Length > p.Max || !p.Pattern.IsMatch(v) || v.StartsWith("primer-", StringComparison.Ordinal)) return SlugCheck.Invalid;
        return p.Reserved.Contains(v) ? SlugCheck.Reserved : SlugCheck.Ok;
    }

    /// <summary>Resource address: pattern + length, then the reserved words of the second segment.</summary>
    public static SlugCheck ValidateResource(string? slug)
    {
        var p = Loaded.Value;
        var v = Normalize(slug);
        if (v.Length < p.ResourceMin || v.Length > p.ResourceMax || !p.ResourcePattern.IsMatch(v)) return SlugCheck.Invalid;
        return p.ReservedResource.Contains(v) ? SlugCheck.Reserved : SlugCheck.Ok;
    }

    private static Policy Load()
    {
        using var stream = typeof(BathsSlugPolicy).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing — check ServiceBooking.API.csproj.");
        using var doc = JsonDocument.Parse(stream);
        var r = doc.RootElement;
        Regex Rx(string name) => new(r.GetProperty(name).GetString()!, RegexOptions.CultureInvariant | RegexOptions.Compiled);
        HashSet<string> Set(string name) => r.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
        return new Policy(Rx("slugPattern"), r.GetProperty("slugMinLength").GetInt32(), r.GetProperty("slugMaxLength").GetInt32(),
            Rx("resourceSlugPattern"), r.GetProperty("resourceSlugMinLength").GetInt32(), r.GetProperty("resourceSlugMaxLength").GetInt32(),
            Set("reservedSlugs"), Set("reservedResourceSlugs"),
            r.GetProperty("spaRoutes").EnumerateArray().Select(e => e.GetString()!).ToList());
    }
}
