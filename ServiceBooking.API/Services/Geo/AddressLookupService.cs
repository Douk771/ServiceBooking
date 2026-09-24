using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Geo;

/// <summary>
/// The one place both geocoder-facing endpoints (<c>POST /api/companies/address/lookup</c> and the
/// verify branch of <c>PUT /api/companies/{id}/address</c>) actually reach the provider
/// (ARCHITECTURE_CYCLE13.md §207/§209.2). Owns the in-memory cache — the LICENSED ceiling, not a
/// performance knob (§209.2):
///
/// 1. capped at <see cref="GeoOptions.CacheHours"/> ≤ 720h (30 days, the standard Yandex licence's own
///    limit on "temporary caching for performance" — a value above that fails startup, see
///    <c>DeploymentSafetyChecks.ValidateAddressVerification</c>);
/// 2. in-process memory ONLY — no table, no file, no Redis; a restart clears it, and that is a FEATURE
///    (nothing in the product is meant to survive a restart having once seen the geocoder's answer);
/// 3. independent of <see cref="GeoOptions.StoreResults"/> — the cache is legal under the standard
///    licence regardless of that flag, which only governs whether coordinates reach the DATABASE.
///
/// Only genuine geocoder answers (<see cref="GeocodeOutcome.Ok"/>/<see cref="GeocodeOutcome.Empty"/>) are
/// cached — caching an <see cref="GeocodeOutcome.Unavailable"/> answer for up to
/// <see cref="GeoOptions.CacheHours"/> would suppress every retry until the window expires, for a
/// transient failure that is exactly the kind of thing a retry a minute later should be able to recover
/// from; nothing in the licence requires caching a NON-result.
/// </summary>
public sealed class AddressLookupService(IAddressGeocoder geocoder, IOptions<GeoOptions> options, IMemoryCache cache)
{
    public async Task<GeocodeResult> LookupAsync(AddressQuery query, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(query);
        if (cache.TryGetValue<GeocodeResult>(cacheKey, out var cached) && cached is not null)
            return cached;

        var result = await geocoder.LookupAsync(query, ct);

        if (result.Outcome is GeocodeOutcome.Ok or GeocodeOutcome.Empty)
        {
            var cacheHours = Math.Max(0, options.Value.CacheHours);
            if (cacheHours > 0)
                cache.Set(cacheKey, result, TimeSpan.FromHours(cacheHours));
        }

        return result;
    }

    /// <summary>Keyed by OUR OWN outbound query (city + address text), never by anything the geocoder
    /// returned — §203's "ключ кеша геокодера — наш запрос".</summary>
    private static string BuildCacheKey(AddressQuery query) =>
        $"geo:{AddressNormalization.Key($"{query.CityName}|{query.Text}")}";
}
