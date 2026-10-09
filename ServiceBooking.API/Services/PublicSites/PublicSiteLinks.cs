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
        var (configured, fallback) = kind switch
        {
            CompanyKind.Services => (_options.ServicesBaseUrl, Defaults.ServicesBaseUrl),
            CompanyKind.Orders => (_options.OrdersBaseUrl, Defaults.OrdersBaseUrl),
            CompanyKind.Stays => (_options.StaysBaseUrl, Defaults.StaysBaseUrl),
            CompanyKind.Baths => (_options.BathsBaseUrl, Defaults.BathsBaseUrl),
            _ => throw new System.Diagnostics.UnreachableException()
        };
        // A blank value (an empty environment variable) means "not configured": the production defaults apply.
        return Trim(string.IsNullOrWhiteSpace(configured) ? fallback : configured);
    }

    private static readonly PublicSitesOptions Defaults = new();

    /// <summary>Salon: {Services}/company/{slug}; shop: {Orders}/{slug}.</summary>
    public string CompanyPageUrl(Company company) => CompanyPageUrl(company.Kind, company.Slug);

    public string CompanyPageUrl(CompanyKind kind, string slug) => kind switch
    {
        CompanyKind.Services => $"{SiteBaseUrl(kind)}/company/{slug}",
        CompanyKind.Orders or CompanyKind.Stays or CompanyKind.Baths => $"{SiteBaseUrl(kind)}/{slug}",
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // ── Cycle 37 (ARCHITECTURE_CYCLE37.md §37.3.2): dom.ezbook.ru ──

    /// <summary>{Stays}/b/{token} — the booking page by link.</summary>
    public string StayBookingPageUrl(string token) => $"{SiteBaseUrl(CompanyKind.Stays)}/b/{token}";

    /// <summary>{Stays}/cabinet/{companyId}/bookings/{bookingId} — the staff's booking card (absolute: a push may open it from another site's worker).</summary>
    public string StaysCabinetBookingUrl(Guid companyId, Guid bookingId) => $"{SiteBaseUrl(CompanyKind.Stays)}/cabinet/{companyId}/bookings/{bookingId}";

    /// <summary>{Stays}/s/{token} — the page of a stand-alone service order.</summary>
    public string StayServiceOrderPageUrl(string token) => $"{SiteBaseUrl(CompanyKind.Stays)}/s/{token}";

    /// <summary>{Stays}/cabinet/{companyId}/service-sessions/{sessionId} — the staff's session card (absolute).</summary>
    public string StaysCabinetServiceSessionUrl(Guid companyId, Guid sessionId) => $"{SiteBaseUrl(CompanyKind.Stays)}/cabinet/{companyId}/service-sessions/{sessionId}";

    /// <summary>{Stays}/{companySlug}/uslugi/{serviceSlug}.</summary>
    public string ServicePageUrl(string companySlug, string serviceSlug) => $"{SiteBaseUrl(CompanyKind.Stays)}/{companySlug}/uslugi/{serviceSlug}";

    /// <summary>{Stays}/cabinet/subscription.</summary>
    public string StaysSubscriptionUrl() => $"{SiteBaseUrl(CompanyKind.Stays)}/cabinet/subscription";

    /// <summary>{Stays}/{companySlug}/{houseSlug}.</summary>
    public string HousePageUrl(string companySlug, string houseSlug) => $"{SiteBaseUrl(CompanyKind.Stays)}/{companySlug}/{houseSlug}";

    // ── Cycle 42 (ARCHITECTURE_CYCLE42.md §42.4.5): links by the kind of a slot vertical («Дома» / «Бани») ──

    /// <summary>{site of kind}/s/{token} — the page of a stand-alone service order of that vertical.</summary>
    public string ServiceOrderPageUrl(CompanyKind kind, string token) => $"{SlotSiteBaseUrl(kind)}/s/{token}";

    /// <summary>/s/{token} — the same page as a path relative to the site (guest push payload).</summary>
    public string ServiceOrderRelativePath(CompanyKind kind, string token) => Slots.SlotVerticals.IsSlotKind(kind) ? $"/s/{token}" : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a slot vertical.");

    /// <summary>{site of kind}/cabinet/{companyId}/service-sessions/{sessionId}.</summary>
    public string CabinetServiceSessionUrl(CompanyKind kind, Guid companyId, Guid sessionId) =>
        $"{SlotSiteBaseUrl(kind)}/cabinet/{companyId}/service-sessions/{sessionId}";

    /// <summary>The public page of a service/resource, by the path of its vertical (Stays: /{company}/uslugi/{service}; Baths: /{company}/{service}).</summary>
    public string ResourcePageUrl(CompanyKind kind, string companySlug, string serviceSlug) =>
        $"{SlotSiteBaseUrl(kind)}{Slots.SlotVerticals.Get(kind).ResourcePagePath(companySlug, serviceSlug)}";

    /// <summary>{site of kind}/cabinet/subscription.</summary>
    public string SlotSubscriptionUrl(CompanyKind kind) => $"{SlotSiteBaseUrl(kind)}/cabinet/subscription";

    private string SlotSiteBaseUrl(CompanyKind kind) =>
        Slots.SlotVerticals.IsSlotKind(kind) ? SiteBaseUrl(kind) : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a slot vertical.");

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
