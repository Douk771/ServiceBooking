using Microsoft.Extensions.Options;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.PublicSites;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §391 — the ONE place that builds absolute links to a company page or an order
/// page. Which site a link belongs to is decided by the company's kind, never by the Host of the request
/// (§400): the same API answers both domains. Cycle 2 (order notifications) must take its links from here.
/// </summary>
public sealed class PublicSiteLinks(IOptions<PublicSitesOptions> options)
{
    private readonly PublicSitesOptions _options = options.Value;

    /// <summary>Base address of the site that serves companies of this kind (no trailing slash).</summary>
    public string SiteBaseUrl(CompanyKind kind)
    {
        var configured = kind == CompanyKind.Orders ? _options.OrdersBaseUrl : _options.ServicesBaseUrl;
        // A blank value (an empty environment variable) means "not configured": the production defaults apply.
        var fallback = kind == CompanyKind.Orders ? Defaults.OrdersBaseUrl : Defaults.ServicesBaseUrl;
        return Trim(string.IsNullOrWhiteSpace(configured) ? fallback : configured);
    }

    private static readonly PublicSitesOptions Defaults = new();

    /// <summary>Salon: {Services}/company/{slug}; shop: {Orders}/{slug}.</summary>
    public string CompanyPageUrl(Company company) => CompanyPageUrl(company.Kind, company.Slug);

    public string CompanyPageUrl(CompanyKind kind, string slug) => kind == CompanyKind.Orders
        ? $"{SiteBaseUrl(kind)}/{slug}"
        : $"{SiteBaseUrl(kind)}/company/{slug}";

    /// <summary>{Orders}/o/{token}.</summary>
    public string OrderPageUrl(string token) => $"{SiteBaseUrl(CompanyKind.Orders)}/o/{token}";

    private static string Trim(string url) => url.TrimEnd('/');
}
