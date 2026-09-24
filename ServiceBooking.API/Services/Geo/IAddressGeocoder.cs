using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Geo;

/// <summary>What the geocoder was asked, and what it actually said (ARCHITECTURE_CYCLE13.md §206/§209).
/// Never carries anything but the address text and city name — see §209.2's "наружу уходит только адрес
/// и город".</summary>
public readonly record struct AddressQuery(string Text, string? CityName);

/// <summary>ARCHITECTURE_CYCLE13.md §209.1's four outcomes — one enum shared by both geocoder-facing
/// endpoints (lookup and save-with-verify).</summary>
public enum GeocodeOutcome { Ok, Empty, Unavailable, Disabled }

public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>One candidate address as the geocoder returned it. This is the GEOCODER'S OWN wording
/// (<see cref="FormattedAddress"/>) — legitimate to show to the owner in the response of a lookup (§233),
/// but it must never be written anywhere: not to <c>Company.Address</c>, not to a new column, not to a
/// log line (§233's own boxed warning; R18).</summary>
public sealed record GeocodeCandidate(string FormattedAddress, AddressPrecision Precision, string? CityName, GeoPoint? Point);

/// <summary>One warning: a machine-readable code the frontend can branch on, and a ready Russian
/// sentence the SERVER composes (ARCHITECTURE_CYCLE13.md §209.1/§236's "текст собирает сервер" — a
/// second copy of the wording on the frontend would drift).</summary>
public sealed record AddressWarning(string Code, string Message);

/// <summary>One geocoding attempt's full result — everything <c>AddressLookupService</c> needs to answer
/// both <c>POST /api/companies/address/lookup</c> and the verify branch of
/// <c>PUT /api/companies/{id}/address</c> (ARCHITECTURE_CYCLE13.md §209).</summary>
public sealed record GeocodeResult(
    GeocodeOutcome Outcome,
    string QueriedAddress,
    IReadOnlyList<GeocodeCandidate> Candidates,
    string? Attribution);

/// <summary>One adapter per provider (ARCHITECTURE_CYCLE13.md §206/§207). Never throws for a "the
/// external service didn't like this" situation — timeouts/network errors/unparsable bodies are all
/// folded into <see cref="GeocodeOutcome.Unavailable"/> by the implementation, so the controller layer
/// never needs a try/catch around a call through this interface (§209's "никогда не 4xx/5xx", R14).</summary>
public interface IAddressGeocoder
{
    Task<GeocodeResult> LookupAsync(AddressQuery query, CancellationToken ct);
}
