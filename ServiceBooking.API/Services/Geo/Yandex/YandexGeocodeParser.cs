using System.Globalization;
using System.Text.Json;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Geo.Yandex;

/// <summary>
/// Pure parser for the Yandex Geocoder's JSON response (ARCHITECTURE_CYCLE13.md §206/§207). No I/O — the
/// adapter (<see cref="YandexAddressGeocoder"/>) owns the HTTP call and the timeout; this class only ever
/// turns a response body it already has into <see cref="GeocodeCandidate"/> rows or reports that it
/// couldn't. Never throws for "the body doesn't look like what we expected" — an unparsable/unexpected
/// shape returns <c>null</c>, which the adapter folds into <see cref="GeocodeOutcome.Unavailable"/> the
/// same way a network error would (§209's "недоступность внешнего сервиса — не ошибка нашей операции").
/// </summary>
public static class YandexGeocodeParser
{
    /// <summary>Parses the top-level <c>response.GeoObjectCollection.featureMember</c> array into
    /// candidates, in the order the geocoder returned them (already relevance-sorted, §233). Returns an
    /// empty (non-null) list for a well-formed "nothing found" response — the caller distinguishes that
    /// from a parse failure by the null-vs-non-null return, not by list emptiness.</summary>
    public static IReadOnlyList<GeocodeCandidate>? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("response", out var response)) return null;
            if (!response.TryGetProperty("GeoObjectCollection", out var collection)) return null;
            if (!collection.TryGetProperty("featureMember", out var featureMember) ||
                featureMember.ValueKind != JsonValueKind.Array)
                return null;

            var candidates = new List<GeocodeCandidate>();
            foreach (var member in featureMember.EnumerateArray())
            {
                var candidate = ParseOne(member);
                if (candidate is not null) candidates.Add(candidate);
            }

            return candidates;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GeocodeCandidate? ParseOne(JsonElement member)
    {
        if (!member.TryGetProperty("GeoObject", out var geoObject)) return null;

        if (!geoObject.TryGetProperty("metaDataProperty", out var metaDataProperty) ||
            !metaDataProperty.TryGetProperty("GeocoderMetaData", out var meta))
            return null;

        var precisionRaw = meta.TryGetProperty("precision", out var precisionEl) ? precisionEl.GetString() : null;
        var kindRaw = meta.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() : null;
        var precision = MapPrecision(precisionRaw, kindRaw);

        string? formattedAddress = null;
        string? cityName = null;
        if (meta.TryGetProperty("Address", out var addressEl))
        {
            if (addressEl.TryGetProperty("formatted", out var formattedEl))
                formattedAddress = formattedEl.GetString();

            if (addressEl.TryGetProperty("Components", out var componentsEl) &&
                componentsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var component in componentsEl.EnumerateArray())
                {
                    if (!component.TryGetProperty("kind", out var componentKind)) continue;
                    if (componentKind.GetString() != "locality") continue;
                    cityName = component.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    break;
                }
            }
        }

        // Fall back to the top-level "text" field if Address.formatted is absent — some responses only
        // carry the plain text summary. Either way, this string is ONLY ever shown to the owner as a
        // candidate to pick from (§233) — it must never be persisted (R18).
        if (string.IsNullOrWhiteSpace(formattedAddress) &&
            meta.TryGetProperty("text", out var textEl))
            formattedAddress = textEl.GetString();

        if (string.IsNullOrWhiteSpace(formattedAddress)) return null;

        var point = ParsePoint(geoObject);

        return new GeocodeCandidate(formattedAddress, precision, cityName, point);
    }

    /// <summary><c>Point.pos</c> is "<c>lon lat</c>" (space-separated, longitude FIRST — the classic
    /// mix-up this codebase guards against with a dedicated unit test elsewhere, mirroring
    /// <c>mapLinks.ts</c>'s own guard on the frontend).</summary>
    private static GeoPoint? ParsePoint(JsonElement geoObject)
    {
        if (!geoObject.TryGetProperty("Point", out var pointEl)) return null;
        if (!pointEl.TryGetProperty("pos", out var posEl)) return null;

        var pos = posEl.GetString();
        if (string.IsNullOrWhiteSpace(pos)) return null;

        var parts = pos.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;

        return new GeoPoint(lat, lon);
    }

    /// <summary>
    /// Yandex's own <c>precision</c> values — <c>exact</c>/<c>number</c>/<c>near</c>/<c>range</c>/
    /// <c>street</c>/<c>other</c> — collapsed to our three-level <see cref="AddressPrecision"/>
    /// (ARCHITECTURE_CYCLE13.md §206). <c>exact</c>/<c>number</c> both mean "a specific building was
    /// matched" → House; <c>near</c>/<c>range</c>/<c>street</c> all mean "a street was matched, no
    /// specific house" → Street; <c>other</c> defers to <c>kind</c> — a locality/district/area/province/
    /// country-level match reads as Locality, anything else as Other. An unrecognized/missing precision
    /// value is treated as Other (fail toward "not verified", never toward "verified").
    /// </summary>
    private static AddressPrecision MapPrecision(string? precision, string? kind) => precision switch
    {
        "exact" or "number" => AddressPrecision.House,
        "near" or "range" or "street" => AddressPrecision.Street,
        "other" => kind is "locality" or "district" or "area" or "province" or "country"
            ? AddressPrecision.Locality
            : AddressPrecision.Other,
        _ => AddressPrecision.Other,
    };
}
