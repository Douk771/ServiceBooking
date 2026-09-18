namespace ServiceBooking.API.Services;

/// <summary>
/// Normalizes a city search query into the same shape <see cref="Core.Entities.City.SearchName"/> was
/// seeded with by the <c>SeedCities</c> migration (ARCHITECTURE_CYCLE4.md §34.2): lowercase, 'ё'→'е',
/// no spaces/hyphens. Pure and framework-free so both the runtime search endpoint and (by hand
/// transliteration, same pattern as <see cref="PhoneNormalizer"/>'s relationship to the phone-
/// normalization migration) the seed data itself stay in agreement about what "the same city" means.
/// </summary>
public static class CitySearch
{
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var lowered = raw.Trim().ToLowerInvariant().Replace('ё', 'е');
        return new string(lowered.Where(c => c != ' ' && c != '-' && c != '(' && c != ')' && c != '—').ToArray());
    }
}
