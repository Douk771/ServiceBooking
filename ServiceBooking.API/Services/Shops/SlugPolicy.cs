using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Shops;

public enum SlugCheck
{
    Ok,
    Invalid,
    Reserved
}

/// <summary>
/// ARCHITECTURE_CYCLE23.md §390 — the address policy of a SHOP (<c>Kind = Orders</c>): pattern, length 3..50, and the
/// reserved words. Read from the embedded contracts/cycle23/goods-routes.json — the ONLY source, shared with the goods
/// frontend and QA. Applies to shops only: salons' slugs are not validated on the server (ezbook behaviour unchanged).
/// Comparison for uniqueness is case-insensitive and the stored form is lowercase — <see cref="Normalize"/>.
/// </summary>
public static class SlugPolicy
{
    public const string ResourceName = "goods-routes.json";

    private sealed record Policy(Regex Pattern, int MinLength, int MaxLength, IReadOnlySet<string> Reserved);

    private static readonly Lazy<Policy> Loaded = new(Load);

    public static int MinLength => Loaded.Value.MinLength;
    public static int MaxLength => Loaded.Value.MaxLength;
    public static IReadOnlySet<string> ReservedSlugs => Loaded.Value.Reserved;

    /// <summary>Trim + lowercase: the stored and compared form of an address.</summary>
    public static string Normalize(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Checks an address (after <see cref="Normalize"/>): format and length first, then the reserved words.</summary>
    public static SlugCheck Validate(string? slug)
    {
        var policy = Loaded.Value;
        var value = Normalize(slug);
        if (value.Length < policy.MinLength || value.Length > policy.MaxLength || !policy.Pattern.IsMatch(value))
            return SlugCheck.Invalid;
        return policy.Reserved.Contains(value) ? SlugCheck.Reserved : SlugCheck.Ok;
    }

    /// <summary>The first path segment of a goods route ("/cabinet/:shopId/orders" → "cabinet"; "/" → null).</summary>
    public static string? FirstSegment(string route) =>
        route.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    private static Policy Load()
    {
        using var stream = typeof(SlugPolicy).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing — check the EmbeddedResource item in ServiceBooking.API.csproj.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var reserved = root.GetProperty("reservedSlugs").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
        return new Policy(
            new Regex(root.GetProperty("slugPattern").GetString()!, RegexOptions.CultureInvariant | RegexOptions.Compiled),
            root.GetProperty("slugMinLength").GetInt32(),
            root.GetProperty("slugMaxLength").GetInt32(),
            reserved);
    }
}
