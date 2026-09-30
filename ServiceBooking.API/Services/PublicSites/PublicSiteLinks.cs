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

    /// <summary>ARCHITECTURE_CYCLE25.md §499.2 — the staff's order card in the cabinet: {Orders}/cabinet/{shopId}/orders?order={orderId}.</summary>
    public string StaffOrdersUrl(Guid shopId, Guid orderId) => $"{SiteBaseUrl(CompanyKind.Orders)}/cabinet/{shopId}/orders?order={orderId}";

    /// <summary>The owner's subscription page in the goods cabinet: {Orders}/cabinet/subscription.</summary>
    public string OrdersSubscriptionUrl() => $"{SiteBaseUrl(CompanyKind.Orders)}/cabinet/subscription";

    /// <summary>{Orders}/o/{token}.</summary>
    public string OrderPageUrl(string token) => $"{SiteBaseUrl(CompanyKind.Orders)}/o/{token}";

    /// <summary>
    /// ARCHITECTURE_CYCLE24.md §457.1 — the unsubscribe link of a message about an order: <c>{ServicesBaseUrl}/u/{token}</c>. The opt-out is by
    /// phone number and common to the whole platform; its page exists on ezbook.ru only (the goods nginx deliberately does not proxy <c>/u/</c>).
    /// </summary>
    public string UnsubscribeUrl(string token) => $"{SiteBaseUrl(CompanyKind.Services)}/u/{token}";

    private static string Trim(string url) => url.TrimEnd('/');
}
