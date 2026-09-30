using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// API_CONTRACT_CYCLE33.md §33.27 — the <c>url</c> of a staff push depends on WHICH site the recipient's subscription belongs to.
/// Same site as the event: the relative path as before the cycle. Other site: an absolute address from <see cref="PublicSiteLinks"/>,
/// so the service worker of that site can hand the click over to the right origin.
/// </summary>
public sealed class StaffPushLinks(PublicSiteLinks siteLinks)
{
    /// <param name="subscriptionSite">Site the receiving subscription was made on.</param>
    /// <param name="eventSite">Site the event (booking / order) lives on.</param>
    /// <param name="relativePath">Path starting with "/", as the event's own site serves it.</param>
    public string ResolveUrl(CompanyKind subscriptionSite, CompanyKind eventSite, string relativePath) =>
        subscriptionSite == eventSite ? relativePath : siteLinks.SiteBaseUrl(eventSite) + relativePath;
}
