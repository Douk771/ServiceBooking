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
        // Postgres' `text` rejects an embedded NUL byte outright (Npgsql throws instead of the ILIKE
        // simply matching nothing), and other control characters can never be part of a real city name
        // anyway — strip the whole Unicode "control" category here, same class of character as
        // Pagination.SanitizeSearch strips for every other `?search=` endpoint (cycle-07 QA finding #2).
        return new string(lowered.Where(c =>
            !char.IsControl(c) && c != ' ' && c != '-' && c != '(' && c != ')' && c != '—').ToArray());
    }
}
