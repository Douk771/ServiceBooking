using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA cycle 13 — a dedicated host for functional tests of the address-verification surface
/// (ARCHITECTURE_CYCLE13.md §207/§209/§214, API_CONTRACT_CYCLE13.md §233/§234/§242). Same reasoning as
/// <see cref="NotificationTestFactory"/>: the shared "Api" collection leaves
/// <c>AddressVerification:Provider = logging</c> (production-safe default), which makes
/// <c>POST /api/companies/address/lookup</c> answer 404 unconditionally and the verify branch of
/// <c>PUT /api/companies/{id}/address</c> a no-op — none of that surface is reachable without a
/// dedicated host that both turns the switch on AND replaces the real network-calling
/// <see cref="ServiceBooking.API.Services.Geo.Yandex.YandexAddressGeocoder"/> with an in-process
/// <see cref="FakeAddressGeocoder"/>, exactly the way the architecture doc asks for (§214: "подменяется
/// только IAddressGeocoder, всё остальное как в бою").
///
/// One instance per test (not shared) — each test gets its own fresh <see cref="Geocoder"/> call log and
/// in-memory cache (a new host means a new <c>IMemoryCache</c> singleton), matching
/// <see cref="RateLimitTestFactory"/>'s own per-test-state reasoning.
/// </summary>
public sealed class AddressVerificationTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly bool _storeResults;
    private readonly int _cacheHours;
    private readonly string _provider;
    private readonly int? _permitLimit;

    public FakeAddressGeocoder Geocoder { get; } = new();

    public TestHostIdentity Identity { get; private set; } = null!;

    /// <param name="connectionString">the class database's connection string, from <see cref="TestDatabaseFixture"/></param>
    /// <param name="storeResults">§209.2/P3 — whether a House-precision verification may write coordinates</param>
    /// <param name="cacheHours">licensed cache ceiling override, default keeps the production default (24h)</param>
    /// <param name="provider">"yandex" (default — exercises the real gated code paths with a fake adapter
    /// underneath) or "logging" (US-140 — rubильник off, endpoint must 404/no-op regardless of what
    /// <see cref="Geocoder"/> would have answered)</param>
    /// <param name="permitLimit">overrides appsettings.Testing.json's raised "address-verify" limit
    /// (10000/min) back down — only set by tests exercising the limiter itself (§210/§238), same
    /// reasoning as <see cref="RateLimitTestFactory"/>.</param>
    public AddressVerificationTestFactory(
        string connectionString, bool storeResults = false, int cacheHours = 24, string provider = "yandex",
        int? permitLimit = null)
    {
        _connectionString = connectionString;
        _storeResults = storeResults;
        _cacheHours = cacheHours;
        _provider = provider;
        _permitLimit = permitLimit;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Identity = TestHostSettings.Apply(builder, "addr", _connectionString);

        builder.UseSetting("AddressVerification:Provider", _provider);
        builder.UseSetting("AddressVerification:StoreResults", _storeResults.ToString());
        builder.UseSetting("AddressVerification:CacheHours", _cacheHours.ToString());
        // Non-empty so DeploymentSafetyChecks.ValidateAddressVerification never fails startup even though
        // this host never actually calls Yandex — the fake sits in front of the real HTTP client entirely.
        builder.UseSetting("AddressVerification:Yandex:ApiKey", "test-key-not-real");
        if (_permitLimit is { } limit)
        {
            builder.UseSetting("RateLimits:address-verify:PermitLimit", limit.ToString());
            builder.UseSetting("RateLimits:address-verify:WindowMinutes", "60");
        }

        // Only substitute the fake when the switch is actually "yandex" — when a test asks for
        // "logging" (US-140), the real DI selection in Program.cs must be exercised unchanged, so it
        // resolves to the real (also fake-free, no-network) LoggingAddressGeocoder instead. Overriding
        // unconditionally would silently defeat every "rubильник off" test: the fake would answer
        // regardless of what the switch says, which is exactly the wrong thing for those tests to
        // observe.
        if (string.Equals(_provider, "yandex", StringComparison.OrdinalIgnoreCase))
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAddressGeocoder>();
                services.AddSingleton<IAddressGeocoder>(Geocoder);
            });
        }
    }
}

/// <summary>
/// In-process double for <see cref="IAddressGeocoder"/> — never touches the network. Counts calls (so
/// tests can assert the cache actually suppressed a second call) and lets a test queue up a scripted
/// answer or an exception per call.
/// </summary>
public sealed class FakeAddressGeocoder : IAddressGeocoder
{
    private readonly Queue<Func<GeocodeResult>> _scripted = new();

    public int CallCount { get; private set; }
    public AddressQuery? LastQuery { get; private set; }

    /// <summary>Default answer when nothing was scripted — one House-precision candidate with the
    /// geocoder's OWN (deliberately different) wording, exactly the shape §218/R18's licence test needs:
    /// a caller who never scripts anything still gets a result whose formatted address must never leak
    /// into the database.</summary>
    public Func<AddressQuery, GeocodeResult> Default { get; set; } = q => Ok(
        $"Россия, {q.CityName ?? "Барнаул"}, проспект Ленина, 5", AddressPrecision.House, q.CityName, new GeoPoint(53.35, 83.78));

    public void Enqueue(GeocodeResult result) => _scripted.Enqueue(() => result);
    public void EnqueueThrow(Exception ex) => _scripted.Enqueue(() => throw ex);

    public Task<GeocodeResult> LookupAsync(AddressQuery query, CancellationToken ct)
    {
        CallCount++;
        LastQuery = query;
        var produce = _scripted.Count > 0 ? _scripted.Dequeue() : () => Default(query);
        // §209's own contract: a bad external answer never throws out of the adapter, it becomes
        // Unavailable. A test that wants to see the controller's "geocoder is down" branch enqueues an
        // Unavailable result directly rather than an exception, but EnqueueThrow exists for the one case
        // that matters (Unavailable IS reachable this way too, folded here for symmetry with the real
        // adapter's own try/catch).
        try
        {
            return Task.FromResult(produce());
        }
        catch
        {
            return Task.FromResult(new GeocodeResult(GeocodeOutcome.Unavailable, query.Text, [], null));
        }
    }

    public static GeocodeResult Ok(string formattedAddress, AddressPrecision precision, string? cityName, GeoPoint? point) =>
        new(GeocodeOutcome.Ok, formattedAddress, [new GeocodeCandidate(formattedAddress, precision, cityName, point)], "Яндекс Карты");

    public static GeocodeResult Empty() => new(GeocodeOutcome.Empty, "", [], null);

    public static GeocodeResult Unavailable() => new(GeocodeOutcome.Unavailable, "", [], null);
}
