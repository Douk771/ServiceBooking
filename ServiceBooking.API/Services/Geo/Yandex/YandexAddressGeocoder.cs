using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Geo.Yandex;

/// <summary>
/// The real provider (<c>AddressVerification:Provider=yandex</c>). Talks to the Yandex Geocoder over the
/// named <c>"yandex-geocoder"</c> HttpClient (ARCHITECTURE_CYCLE13.md §206) — keep-alive + IPv4-first
/// handler from <see cref="GeoHandlerFactory"/>, request/URL logging silenced at the category level in
/// <c>Program.cs</c> (rung 1 of the defence against a leaked <c>apikey</c>).
///
/// Never throws outward for anything the external service did or didn't do — timeout, network failure,
/// non-200 status, or an unparsable body all fold into <see cref="GeocodeOutcome.Unavailable"/> (§209's
/// "никогда не 4xx/5xx", R14). Logs only <see cref="YandexGeocoderUrls.SafeLabel"/>, NEVER the request
/// <see cref="Uri"/> and NEVER the response body — the body is the geocoder's "result", and a log is
/// storage the standard licence does not grant (§206, R15).
/// </summary>
public sealed class YandexAddressGeocoder(
    IHttpClientFactory httpClientFactory, IOptions<GeoOptions> options, ILogger<YandexAddressGeocoder> logger)
    : IAddressGeocoder
{
    /// <summary>The one literal attribution string this adapter ever produces — shown to the owner
    /// alongside candidates while they're on screen (API_CONTRACT_CYCLE13.md §233), never invented by the
    /// frontend (§211).</summary>
    private const string AttributionText = "© Яндекс";

    public async Task<GeocodeResult> LookupAsync(AddressQuery query, CancellationToken ct)
    {
        var geoOptions = options.Value;
        var queriedAddress = BuildQueriedAddress(query);

        var (request, safeLabel) = YandexGeocoderUrls.Lookup(
            geoOptions.Yandex.ApiUrl, geoOptions.Yandex.ApiKey, queriedAddress, geoOptions.MaxCandidates);

        var client = httpClientFactory.CreateClient("yandex-geocoder");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(geoOptions.Yandex.TimeoutSeconds));

        string body;
        try
        {
            using var response = await client.GetAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Yandex geocoder returned {StatusCode} for {SafeLabel}", (int)response.StatusCode, safeLabel);
                return Unavailable(queriedAddress);
            }

            body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw; // the CALLER's own cancellation, not our timeout — propagate
            logger.LogWarning(ex, "Yandex geocoder call failed for {SafeLabel}", safeLabel);
            return Unavailable(queriedAddress);
        }

        var candidates = YandexGeocodeParser.Parse(body);
        if (candidates is null)
        {
            logger.LogWarning("Yandex geocoder returned an unparsable body for {SafeLabel}", safeLabel);
            return Unavailable(queriedAddress);
        }

        var limited = candidates.Take(geoOptions.MaxCandidates).ToList();
        var outcome = limited.Count > 0 ? GeocodeOutcome.Ok : GeocodeOutcome.Empty;
        return new GeocodeResult(outcome, queriedAddress, limited, AttributionText);
    }

    /// <summary>"город, адрес" — the same order §205's frontend map-link builder uses, so the string the
    /// owner sees in <c>queriedAddress</c> (§233) reads the way they'd expect.</summary>
    private static string BuildQueriedAddress(AddressQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.CityName)) parts.Add(query.CityName.Trim());
        if (!string.IsNullOrWhiteSpace(query.Text)) parts.Add(query.Text.Trim());
        return string.Join(", ", parts);
    }

    private static GeocodeResult Unavailable(string queriedAddress) =>
        new(GeocodeOutcome.Unavailable, queriedAddress, [], Attribution: null);
}
