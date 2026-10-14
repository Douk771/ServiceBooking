namespace ServiceBooking.API.Services.PublicSites;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §391 — public base addresses of the two sites. The production values are the
/// committed defaults in appsettings.json, so no new mandatory environment variable exists; dev overrides
/// them via PublicSites__* (see .env.dev.example). Validated by DeploymentSafetyChecks.ValidatePublicSites.
/// </summary>
public sealed class PublicSitesOptions
{
    public const string SectionName = "PublicSites";

    /// <summary>ezbook.ru — salons, without a trailing slash.</summary>
    public string ServicesBaseUrl { get; set; } = "https://ezbook.ru";

    /// <summary>goods.ezbook.ru — shops, without a trailing slash.</summary>
    public string OrdersBaseUrl { get; set; } = "https://goods.ezbook.ru";

    /// <summary>dom.ezbook.ru — "Дома" (ARCHITECTURE_CYCLE37.md §37.15.3), without a trailing slash. A committed default: no new mandatory variable.</summary>
    public string StaysBaseUrl { get; set; } = "https://dom.ezbook.ru";

    /// <summary>bani.ezbook.ru — «Бани» (ARCHITECTURE_CYCLE42.md §42.4.5), without a trailing slash. A committed default: no new mandatory variable.</summary>
    public string BathsBaseUrl { get; set; } = "https://bani.ezbook.ru";
}
