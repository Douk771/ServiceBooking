using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.3.5 — the ONE call that drops both in-process caches that hold the price/activity of the messenger options
/// (the public price list and the catalog rows of <see cref="AccountMessagingReader"/>). Called AFTER the commit of every write to the
/// options catalog, so a request started after the response of that write sees the new price everywhere.
/// </summary>
public static class MessengerCatalogCaches
{
    public static void Invalidate(PricingCatalogCache pricingCatalogCache, IMemoryCache cache)
    {
        pricingCatalogCache.Invalidate();
        AccountMessagingReader.InvalidateCatalogCache(cache);
    }
}
