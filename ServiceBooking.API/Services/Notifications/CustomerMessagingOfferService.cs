using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>The public answer "do messenger messages work for this company's customers" (API_CONTRACT_CYCLE40.md §40.31): only the boolean, the transports and the
/// ready checkbox label — never a number, a state, a payment or a reason.</summary>
public sealed record CustomerMessagingOfferDto(bool Offered, IReadOnlyList<NotificationTransport> Transports, string? CheckboxLabel)
{
    public static CustomerMessagingOfferDto From(CustomerMessagingOfferResult r) => new(r.Offered, r.Transports, r.CheckboxLabel);
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.10 — gathers the facts for <see cref="CustomerMessagingOfferRule"/> for a salon, a shop or a «Дома» company. The answer is cached for
/// 30 seconds per company (public pages are read far more often than a number changes); creating a record re-checks without the cache
/// (<see cref="EvaluateAsync"/> with <c>useCache: false</c>).
/// </summary>
public sealed class CustomerMessagingOfferService(
    AppDbContext db, AccountMessagingReader messagingReader, IMemoryCache cache, IOptions<DemoModeOptions> demoOptions)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<CustomerMessagingOfferDto> ForCompanyAsync(Company company, bool useCache = true, CancellationToken ct = default)
    {
        var key = $"customer-messaging-offer:{company.Id}";
        if (useCache && cache.TryGetValue(key, out CustomerMessagingOfferDto? cached) && cached is not null) return cached;

        var offer = CustomerMessagingOfferDto.From(await EvaluateAsync(company, ct));
        cache.Set(key, offer, CacheDuration);
        return offer;
    }

    public async Task<CustomerMessagingOfferResult> EvaluateAsync(Company company, CancellationToken ct = default)
    {
        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct)
            ?? new CompanyNotificationSettings();
        var flag = company.Kind switch
        {
            CompanyKind.Services => (settings.EnabledTypeMask & ((1 << (int)NotificationType.BookingConfirmed) | (1 << (int)NotificationType.Reminder))) != 0,
            CompanyKind.Orders => await db.ShopSettings.AsNoTracking().Where(s => s.CompanyId == company.Id).Select(s => s.CustomerMessengerEnabled).FirstOrDefaultAsync(ct),
            CompanyKind.Stays or CompanyKind.Baths => await db.StaysSettings.AsNoTracking().Where(s => s.CompanyId == company.Id).Select(s => s.GuestMessengerEnabled).FirstOrDefaultAsync(ct),
            _ => false,
        };
        var messaging = await messagingReader.ForCompanyAsync(company.Id, ct: ct);
        return CustomerMessagingOfferRule.Evaluate(new CustomerMessagingOfferInput(
            messaging.PlatformEnabled && !ShowcaseOutboundGuard.IsSuppressed(company, demoOptions.Value.Enabled), company.Kind, flag, company.IsShowcase, company.IsActive,
            settings.DeliveryMode, settings.PriorityTransport,
            messaging.Transports.Select(t => new OfferTransportFacts(t.Transport, t.Routable, t.Working)).ToList()));
    }
}
