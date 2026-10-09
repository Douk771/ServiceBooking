using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.4 — address policy of a "Дома" company and of a house, from the embedded contracts/cycle39/dom-routes.json (superset of cycle 37)
/// (the one source shared with the dom frontend and QA). Company slugs share the platform-wide space; <c>primer-</c> is reserved for showcase shops.
/// </summary>
public static class StaysSlugPolicy
{
    public const string ResourceName = "dom-routes.json";

    private sealed record Policy(Regex Pattern, int Min, int Max, Regex HousePattern, int HouseMin, int HouseMax,
        IReadOnlySet<string> Reserved, IReadOnlyList<string> SpaRoutes, IReadOnlySet<string> ReservedHouse, Regex ServicePattern, int ServiceMin, int ServiceMax);

    private static readonly Lazy<Policy> Loaded = new(Load);

    public static IReadOnlySet<string> ReservedSlugs => Loaded.Value.Reserved;
    public static IReadOnlyList<string> SpaRoutes => Loaded.Value.SpaRoutes;
    public static IReadOnlySet<string> ReservedHouseSlugs => Loaded.Value.ReservedHouse;

    /// <summary>ARCHITECTURE_CYCLE39.md §39.14.1 — a house cannot take a word the address space holds for /uslugi/… and for the calendars of cycle 40.</summary>
    public static bool IsReservedHouseSlug(string? slug) => Loaded.Value.ReservedHouse.Contains(Normalize(slug));

    public static bool IsValidServiceSlug(string? slug)
    {
        var p = Loaded.Value;
        var v = Normalize(slug);
        return v.Length >= p.ServiceMin && v.Length <= p.ServiceMax && p.ServicePattern.IsMatch(v);
    }

    public static string Normalize(string? slug) => SlugPolicy.Normalize(slug);

    public static SlugCheck Validate(string? slug)
    {
        var p = Loaded.Value;
        var v = Normalize(slug);
        if (v.Length < p.Min || v.Length > p.Max || !p.Pattern.IsMatch(v) || v.StartsWith("primer-", StringComparison.Ordinal)) return SlugCheck.Invalid;
        return p.Reserved.Contains(v) ? SlugCheck.Reserved : SlugCheck.Ok;
    }

    public static bool IsValidHouseSlug(string? slug)
    {
        var p = Loaded.Value;
        var v = Normalize(slug);
        return v.Length >= p.HouseMin && v.Length <= p.HouseMax && p.HousePattern.IsMatch(v);
    }

    private static Policy Load()
    {
        using var stream = typeof(StaysSlugPolicy).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing — check ServiceBooking.API.csproj.");
        using var doc = JsonDocument.Parse(stream);
        var r = doc.RootElement;
        Regex Rx(string name) => new(r.GetProperty(name).GetString()!, RegexOptions.CultureInvariant | RegexOptions.Compiled);
        return new Policy(Rx("slugPattern"), r.GetProperty("slugMinLength").GetInt32(), r.GetProperty("slugMaxLength").GetInt32(),
            Rx("houseSlugPattern"), r.GetProperty("houseSlugMinLength").GetInt32(), r.GetProperty("houseSlugMaxLength").GetInt32(),
            r.GetProperty("reservedSlugs").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal),
            r.GetProperty("spaRoutes").EnumerateArray().Select(e => e.GetString()!).ToList(),
            r.TryGetProperty("reservedHouseSlugs", out var rh) ? rh.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(),
            r.TryGetProperty("serviceSlugPattern", out _) ? Rx("serviceSlugPattern") : Rx("houseSlugPattern"),
            r.TryGetProperty("serviceSlugMinLength", out var smin) ? smin.GetInt32() : 2, r.TryGetProperty("serviceSlugMaxLength", out var smax) ? smax.GetInt32() : 50);
    }
}
