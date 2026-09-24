namespace ServiceBooking.API.Services.Geo;

/// <summary>
/// Typed binding of the <c>AddressVerification</c> configuration section (ARCHITECTURE_CYCLE13.md §206),
/// built as a direct copy of <c>NotificationOptions</c>'s shape (same "logging | yandex, unrecognized
/// value fails startup" convention).
/// </summary>
public sealed class GeoOptions
{
    public const string SectionName = "AddressVerification";

    /// <summary><c>"logging"</c> (default, safe everywhere — no network call, ever) or
    /// <c>"yandex"</c> (real provider, requires <see cref="YandexOptions.ApiKey"/> and is refused
    /// outside Development by <c>DeploymentSafetyChecks.ValidateAddressVerification</c>). Any other
    /// value fails startup, always — this is the one thing that is NOT environment-gated.</summary>
    public string Provider { get; set; } = "logging";

    /// <summary>P3 (§209.2) — whether a successful <c>House</c>-precision verification may write
    /// <see cref="Core.Entities.Company.AddressLatitude"/>/<c>AddressLongitude</c> and expose
    /// <c>addressPoint</c> in the API. Only legitimate under Yandex's EXTENDED ("with result storage")
    /// licence — the code cannot verify that a human bought the right licence, so it only warns
    /// (<c>DeploymentSafetyChecks.ValidateAddressVerification</c>), it does not block. Default false: the
    /// product is fully functional with this off (§209.2's own promise).</summary>
    public bool StoreResults { get; set; }

    public int MaxCandidates { get; set; } = 5;

    /// <summary>Licensed ceiling is 720 hours (30 days, LEGAL_REVIEW.md §16.2/§16.5) — a value outside
    /// [0, 720] fails startup ALWAYS, not just outside Development; see this option's own doc comment on
    /// <see cref="DeploymentSafetyChecks.ValidateAddressVerification"/> for why that check is
    /// unconditional. Default 24h leaves headroom under the ceiling for a one-line config change with no
    /// legal conversation needed.</summary>
    public int CacheHours { get; set; } = 24;

    public YandexOptions Yandex { get; set; } = new();

    public sealed class YandexOptions
    {
        public string ApiUrl { get; set; } = "https://geocode-maps.yandex.ru/1.x/";

        /// <summary>Server-side secret. Empty in git; never sent to the browser, never logged (the
        /// named HttpClient's category-level filter and <c>YandexGeocoderUrls.SafeLabel</c> are the two
        /// independent rungs of that defence, §206/R15), never echoed back in any API response.</summary>
        public string ApiKey { get; set; } = "";

        public int TimeoutSeconds { get; set; } = 5;

        /// <summary>Same "IPv4First" / "System" escape hatch as
        /// <c>NotificationOptions.GreenApiOptions.ConnectPreference</c> — the boxed machine has no global
        /// IPv6 route (§28.1's original reasoning, reused verbatim here via
        /// <c>Notifications.PreferIPv4</c>).</summary>
        public string ConnectPreference { get; set; } = "IPv4First";

        public int ConnectTimeoutSeconds { get; set; } = 5;
        public int PerAddressConnectTimeoutSeconds { get; set; } = 2;
    }
}
