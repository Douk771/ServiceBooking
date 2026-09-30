using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.API.DTOs.Catalog;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §505.3, API_CONTRACT_CYCLE25.md §531 — the anonymous catalog of shops by city on the goods home page. Visibility is ONE rule
/// (<see cref="CatalogListingRules"/>): candidates by one SQL, the tariff filter in memory, the acceptance state of the whole page in four queries via
/// <see cref="ShopGateLoader.LoadManyAsync"/> and the SAME <see cref="ShopOrderingGate.Evaluate"/> as the storefront (so the catalog cannot disagree with
/// the shop page about "takes orders"). The list of a city is cached for <c>Orders:CatalogCacheSeconds</c> (30 s); search, "open now", ordering and
/// the page are applied to it in memory. A hidden shop never appears — not as a card, not in <c>totalCount</c>, not in search. No prices, photos, ratings
/// or seller details [legal L19]; the customer is never told the exact reason a shop is not accepting.
/// </summary>
public sealed class GoodsCatalogService(
    AppDbContext db, ShopGateLoader gates, OrdersPlanResolver plans, IMemoryCache cache, IConfiguration configuration,
    ServiceBooking.API.Services.Notifications.INotificationClock clock)
{
    public const string EmptyCityText = "В этом городе пока нет магазинов на goods";
    public const string EmptyAllText = "Пока нет магазинов на goods";
    public const string NothingFoundText = "Ничего не найдено";

    /// <summary>A shop as the cached list of a city holds it (no personal or commercial data).</summary>
    public sealed record CatalogEntry(
        Guid Id, string Slug, string Name, string? Address, string CityName, ShopOpenStateDto OpenState, CatalogAcceptance Acceptance);

    public int PageSize => Math.Max(1, configuration.GetValue("Orders:CatalogPageSize", 20));

    public async Task<GoodsCatalogPageDto> GetAsync(int? cityId, bool openNow, string? search, int? page, CancellationToken ct)
    {
        var all = await ListOfCityAsync(cityId, ct);

        var query = GoodsCatalogOrdering.NormalizeSearch(search);
        IEnumerable<CatalogEntry> filtered = all;
        if (openNow) filtered = filtered.Where(e => e.OpenState.IsOpen);
        if (query is not null) filtered = filtered.Where(e => GoodsCatalogOrdering.Matches(e.Name, e.Address, query));
        var matching = filtered.ToList();

        var pageNumber = GoodsCatalogOrdering.NormalizePage(page);
        var pageSize = PageSize;
        var items = matching.Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(e => new GoodsCatalogShopDto(
                e.Slug, "/" + e.Slug, e.Name, e.Address, e.CityName, e.OpenState, e.Acceptance, GoodsCatalogOrdering.Text(e.Acceptance))).ToList();

        CatalogCityDto? city = null;
        if (cityId is { } id)
        {
            var row = await db.Cities.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.Id, c.Name, c.Region }).FirstOrDefaultAsync(ct);
            if (row is not null) city = new CatalogCityDto(row.Id, row.Name, row.Region, $"{row.Name}, {row.Region}");
        }

        var emptyText = matching.Count > 0 ? null
            : openNow || query is not null ? NothingFoundText
            : cityId is not null ? EmptyCityText
            : EmptyAllText;
        return new GoodsCatalogPageDto(items, pageNumber, pageSize, matching.Count, city, emptyText);
    }

    /// <summary>The ordered list of visible shops of a city (or of all cities), cached per city.</summary>
    private async Task<List<CatalogEntry>> ListOfCityAsync(int? cityId, CancellationToken ct)
    {
        var seconds = configuration.GetValue("Orders:CatalogCacheSeconds", 30);
        if (seconds <= 0) return await BuildAsync(cityId, ct);

        var key = $"goods-catalog:{(cityId is { } id ? id.ToString() : "all")}";
        if (cache.TryGetValue(key, out List<CatalogEntry>? cached) && cached is not null) return cached;

        var built = await BuildAsync(cityId, ct);
        cache.Set(key, built, TimeSpan.FromSeconds(seconds));
        return built;
    }

    private async Task<List<CatalogEntry>> BuildAsync(int? cityId, CancellationToken ct)
    {
        var nowUtc = clock.UtcNow;

        // 1. Candidates — one SQL: the shop conditions of §505.1 that the database can see (active, opted in, hours set, a published product).
        var candidates = await db.Companies.AsNoTracking().Include(c => c.City)
            .Where(c => c.Kind == CompanyKind.Orders && c.IsActive && c.ShowInPublicListing)
            .Where(c => cityId == null || c.CityId == cityId)
            .Where(c => db.ShopSettings.Any(s => s.CompanyId == c.Id && s.WorkingHoursJson != null))
            .Where(c => db.Products.Any(p => p.CompanyId == c.Id && p.IsPublished && p.DeletedAtUtc == null))
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        // 2. The tariff (two queries for all accounts); the shops whose plan does not allow a listing are dropped by the one rule.
        var accountIds = candidates.Where(c => c.BillingAccountId != null).Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        var planByAccount = accountIds.Count == 0 ? new Dictionary<Guid, OrdersPlan>() : await plans.GetForAccountsAsync(accountIds, ct);
        var visible = candidates.Where(c =>
        {
            var plan = c.BillingAccountId is { } a ? planByAccount[a] : OrdersPlan.FallbackFree;
            return CatalogListingRules.Evaluate(new CatalogListingInput(c.IsActive, true, true, plan.AllowPublicListing, c.ShowInPublicListing)).Visible;
        }).ToList();
        if (visible.Count == 0) return [];

        // 3. The acceptance state of every visible shop — the same function as the shop page.
        var contexts = await gates.LoadManyAsync(visible, nowUtc, ct, plansByAccount: planByAccount);

        var entries = visible.Select(c =>
        {
            var gate = contexts[c.Id].Gate;
            var open = gate.OpenState;
            return new CatalogEntry(
                c.Id, c.Slug, c.Name, string.IsNullOrWhiteSpace(c.Address) ? null : c.Address, c.City?.Name ?? string.Empty,
                new ShopOpenStateDto(open.IsOpen, open.Text, open.OpensAtUtc, open.ClosesAtUtc),
                GoodsCatalogOrdering.Classify(gate.Accepting, gate.Asap.Available, gate.ScheduledAvailable));
        });
        return GoodsCatalogOrdering.Order(entries, e => e.Acceptance, e => e.Name, e => e.Id);
    }
}
