namespace ServiceBooking.API.Services.Geo;

/// <summary>
/// Default provider (<c>AddressVerification:Provider=logging</c>). Makes NO network call, ever — this is
/// the intended, shipped-and-running-in-production state after this cycle (P2, ARCHITECTURE_CYCLE13.md
/// §206), not a temporary stub.
///
/// The controller layer never actually calls this for <c>POST /api/companies/address/lookup</c> (that
/// route answers 404 outright while the switch is off, §209/§233) — this adapter only matters for the
/// verify branch of <c>PUT /api/companies/{id}/address</c>, where <c>outcome: Disabled</c> must reach the
/// caller as part of an ordinary 200 (the save itself is never blocked by the switch being off, §234).
/// </summary>
public sealed class LoggingAddressGeocoder : IAddressGeocoder
{
    public Task<GeocodeResult> LookupAsync(AddressQuery query, CancellationToken ct) =>
        Task.FromResult(new GeocodeResult(GeocodeOutcome.Disabled, query.Text, [], Attribution: null));
}
