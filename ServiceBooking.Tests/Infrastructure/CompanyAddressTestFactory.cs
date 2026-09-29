using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE19.md §388.1/§388.4 (renamed from <c>AddressVerificationTestFactory</c>, tag
/// "addr" kept) — a dedicated host for functional tests of company address saving and the
/// public-address-notice legal gate. Cycle 13's geocoder ("проверка адреса по карте") is removed
/// целиком; this factory no longer fakes an <c>IAddressGeocoder</c> or flips a Provider switch — there
/// is nothing left to fake. What remains: the ability to override the shared "address-verify" rate
/// limit (§388.2, same reasoning as <see cref="RateLimitTestFactory"/>), and an optional bag of raw
/// config overrides for ADDR-030 (starting a host with the OLD, cycle-13 geocoder settings present in
/// the environment — a legacy `.env` that hasn't been cleaned up yet — must still start and save).
/// </summary>
public sealed class CompanyAddressTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly int? _permitLimit;
    private readonly IReadOnlyDictionary<string, string?> _extraSettings;

    public TestHostIdentity Identity { get; private set; } = null!;

    /// <param name="connectionString">the class database's connection string, from <see cref="TestDatabaseFixture"/></param>
    /// <param name="permitLimit">overrides appsettings.Testing.json's raised "address-verify" limit
    /// (10000/min) back down — only set by tests exercising the limiter itself (§388.2).</param>
    /// <param name="extraSettings">ADDR-030 (§388.4) — arbitrary additional config keys/values applied
    /// via <see cref="IWebHostBuilder.UseSetting"/>, e.g. the old, now-unread cycle-13
    /// <c>AddressVerification:*</c> keys at their worst legacy values, to prove the host still starts
    /// and still saves an address with them present.</param>
    public CompanyAddressTestFactory(
        string connectionString, int? permitLimit = null,
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        _connectionString = connectionString;
        _permitLimit = permitLimit;
        _extraSettings = extraSettings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Identity = TestHostSettings.Apply(builder, "addr", _connectionString);

        if (_permitLimit is { } limit)
        {
            builder.UseSetting("RateLimits:address-verify:PermitLimit", limit.ToString());
            builder.UseSetting("RateLimits:address-verify:WindowMinutes", "60");
        }

        foreach (var (key, value) in _extraSettings)
            builder.UseSetting(key, value);
    }
}
