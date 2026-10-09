using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Baths;

/// <summary>One bookable resource of the catalog «base»: everything the card and the date filter need, with no per-request queries.</summary>
public sealed record CatalogResource(
    ServiceScope Scope, int CityId, string CityName, string? Region, string? CoverUrl, string? CoverThumbUrl, int? PriceFromRub)
{
    public StayService Service => Scope.Service;
    public Company Company => Scope.Company;
}

/// <summary>Pure rules of the catalog query (testable without a database).</summary>
public static class BathsCatalogRules
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 50;
    public const string CityNotFoundText = "Город не найден";
    public const string BadDateText = "Неверный формат даты";
    public const string PastDateText = "Эта дата уже прошла";
    public const string EmptyCityText = "В этом городе пока нет бань";
    public const string EmptyDateText = "На эту дату свободного времени нет";

    public static int ClampPageSize(int? pageSize) => Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

    /// <summary>The 400 sentence for a bad <paramref name="raw"/> date, or null (<paramref name="date"/> is then set). A date is past only if it is past in EVERY zone of the base (<paramref name="earliestToday"/>).</summary>
    public static string? ParseDate(string? raw, DateOnly earliestToday, out DateOnly date)
    {
        if (!StaysCatalogService.TryDate(raw, out date)) return BadDateText;
        return date < earliestToday ? PastDateText : null;
    }

    /// <summary>City (by name), company (by name), resource (by <c>Position</c>); ties by name and id so the order is stable between pages.</summary>
    public static List<CatalogResource> Order(IEnumerable<CatalogResource> items) => items
        .OrderBy(r => r.CityName, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(r => r.Company.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(r => r.Company.Id)
        .ThenBy(r => r.Service.Position)
        .ThenBy(r => r.Service.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(r => r.Service.Id).ToList();

    public static string UrlOf(CatalogResource r) => SlotVerticals.Baths.ResourcePagePath(r.Company.Slug, r.Service.Slug);
}

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.10.2, API_CONTRACT_CYCLE42.md §42.22, §42.23 — the public catalog of «Бани» and the complex page. Only the «base» (published, non-archived
/// resources of active, listed «Бани» companies whose gate is open with the resource's own prepayment) is cached, for <c>Baths:CatalogCacheSeconds</c>; occupancy is NEVER
/// cached (the date filter is computed per request by the same calculator as the resource page) and a booking is re-checked under the resource lock, so the cache cannot
/// produce a double booking.
/// </summary>
public class BathsCatalogService(
    AppDbContext db, IMemoryCache cache, IOptions<BathsOptions> options, IStaysClock clock, ServiceSlotService slots, StaysCompanyService companyService)
{
    private const string BaseKey = "baths:catalog-base";

    /// <summary>Drops the cached base: publication, unpublishing, archive, deletion of a resource, a blocked company, the «show in catalog» switch.</summary>
    public void InvalidateBase() => cache.Remove(BaseKey);

    // ── the base ──

    private async Task<IReadOnlyList<CatalogResource>> LoadBaseAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(BaseKey, out IReadOnlyList<CatalogResource>? cached) && cached is not null) return cached;
        var built = await BuildBaseAsync(ct);
        cache.Set(BaseKey, built, TimeSpan.FromSeconds(Math.Max(1, options.Value.CatalogCacheSeconds)));
        return built;
    }

    private async Task<IReadOnlyList<CatalogResource>> BuildBaseAsync(CancellationToken ct)
    {
        var rows = await (from s in db.StayServices.AsNoTracking()
                          join c in db.Companies.AsNoTracking().Include(c => c.City) on s.CompanyId equals c.Id
                          where s.IsPublished && s.ArchivedAtUtc == null && c.Kind == CompanyKind.Baths && c.IsActive && c.ShowInPublicListing && c.City != null
                          select new { Service = s, Company = c }).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var settings = await SettingsOfAsync(rows.Select(r => r.Company.Id).Distinct().ToList(), ct);
        var shaped = await ShapeAsync(rows.Select(r => new ServiceScope(r.Service, r.Company, settings.GetValueOrDefault(r.Company.Id) ?? new StaysSettings { CompanyId = r.Company.Id })).ToList(), ct);

        // the gate of each company with the prepayment of THIS resource, the tariff counted per billing ACCOUNT (a batch: three queries for the whole base)
        var now = clock.UtcNow;
        var accountIds = rows.Select(r => r.Company.BillingAccountId).Where(a => a != null).Select(a => a!.Value).Distinct().ToList();
        var subs = await db.BathsSubscriptions.AsNoTracking().Include(s => s.PlanConfig).Where(s => accountIds.Contains(s.BillingAccountId)).ToDictionaryAsync(s => s.BillingAccountId, ct);
        var published = await (from s in db.StayServices.AsNoTracking()
                               join c in db.Companies.AsNoTracking() on s.CompanyId equals c.Id
                               where s.IsPublished && s.ArchivedAtUtc == null && c.Kind == CompanyKind.Baths && c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId.Value)
                               group s by c.BillingAccountId!.Value into g
                               select new { Account = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Account, x => x.Count, ct);
        var result = new List<CatalogResource>(shaped.Count);
        foreach (var r in shaped)
        {
            var company = r.Company;
            var plan = StaysPlanResolver.ResolveBaths(company.BillingAccountId is { } a ? subs.GetValueOrDefault(a) : null, now);
            var count = company.BillingAccountId is { } acc ? published.GetValueOrDefault(acc) : 0;
            var gate = StaysBookingGate.Evaluate(company.IsActive, plan.HasActivePlan, count, plan.MaxUnits, r.Service.StandalonePrepayPercent ?? 0,
                r.Scope.Settings.PaymentDetails, StaysCompanyService.ProviderFacts(r.Scope.Settings), GateUnit.Resource);
            if (gate.Accepting) result.Add(r);
        }
        return result;
    }

    private async Task<Dictionary<Guid, StaysSettings>> SettingsOfAsync(List<Guid> companyIds, CancellationToken ct) =>
        await db.StaysSettings.AsNoTracking().Where(s => companyIds.Contains(s.CompanyId)).ToDictionaryAsync(s => s.CompanyId, ct);

    /// <summary>The covers (the resource's first photo, else the complex's first photo) and the price «from» of each resource.</summary>
    private async Task<List<CatalogResource>> ShapeAsync(List<ServiceScope> scopes, CancellationToken ct)
    {
        if (scopes.Count == 0) return [];
        var ids = scopes.Select(s => s.Service.Id).ToList();
        var companyIds = scopes.Select(s => s.Company.Id).Distinct().ToList();
        var covers = (await db.StayServicePhotos.AsNoTracking().Where(p => ids.Contains(p.ServiceId)).OrderBy(p => p.Position).Select(p => new { p.ServiceId, p.Url, p.ThumbnailUrl }).ToListAsync(ct))
            .GroupBy(p => p.ServiceId).ToDictionary(g => g.Key, g => g.First());
        var companyCovers = (await db.CompanyPhotos.AsNoTracking().Where(p => companyIds.Contains(p.CompanyId)).OrderBy(p => p.Position).Select(p => new { p.CompanyId, p.Url, p.ThumbnailUrl }).ToListAsync(ct))
            .GroupBy(p => p.CompanyId).ToDictionary(g => g.Key, g => g.First());
        var minPrice = (await db.StayServicePriceRules.AsNoTracking().Where(r => ids.Contains(r.ServiceId)).Select(r => new { r.ServiceId, r.PriceRub }).ToListAsync(ct))
            .GroupBy(r => r.ServiceId).ToDictionary(g => g.Key, g => g.Min(r => r.PriceRub));
        var cityIds = scopes.Where(s => s.Company.CityId != null).Select(s => s.Company.CityId!.Value).Distinct().ToList();
        var cities = await db.Cities.AsNoTracking().Where(c => cityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return scopes.Select(scope =>
        {
            var city = scope.Company.CityId is { } cid ? cities.GetValueOrDefault(cid) : null;
            string? url = null, thumb = null;
            if (covers.TryGetValue(scope.Service.Id, out var own)) { url = own.Url; thumb = own.ThumbnailUrl ?? own.Url; }
            else if (companyCovers.TryGetValue(scope.Company.Id, out var shared)) { url = shared.Url; thumb = string.IsNullOrEmpty(shared.ThumbnailUrl) ? shared.Url : shared.ThumbnailUrl; }
            return new CatalogResource(scope, city?.Id ?? 0, city?.Name ?? string.Empty, string.IsNullOrWhiteSpace(city?.Region) ? null : city!.Region, url, thumb,
                minPrice.TryGetValue(scope.Service.Id, out var p) ? p : null);
        }).ToList();
    }

    public static BathResourceCardDto ToCard(CatalogResource r) => new(
        r.Service.Id, r.Service.Slug, r.Service.Name, r.Company.Slug, r.Company.Name, r.Company.Address, r.CityName, r.CoverUrl, r.CoverThumbUrl, r.PriceFromRub,
        r.Service.MinHours, r.Service.Capacity, BathsCatalogRules.UrlOf(r));

    // ── the catalog ──

    public async Task<(ActionResult? Error, BathsCatalogPageDto? Page)> CatalogAsync(int? cityId, string? dateRaw, int? page, int? pageSize, CancellationToken ct)
    {
        City? city = null;
        if (cityId is { } requested)
        {
            city = await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == requested && c.IsActive, ct);
            if (city is null) return (new BadRequestObjectResult(BathsCatalogRules.CityNotFoundText), null);
        }
        var all = await LoadBaseAsync(ct);
        var filterByDate = !string.IsNullOrWhiteSpace(dateRaw);
        DateOnly date = default;
        if (filterByDate)
        {
            var earliestToday = all.Count == 0 ? DateOnly.FromDateTime(clock.UtcNow.AddHours(-12)) : all.Min(r => slots.TodayOf(r.Company));
            var dateError = BathsCatalogRules.ParseDate(dateRaw, earliestToday, out date);
            if (dateError is not null) return (new BadRequestObjectResult(dateError), null);
        }

        var candidates = cityId is null ? all : all.Where(r => r.CityId == cityId).ToList();
        if (filterByDate)
        {
            var withStarts = await slots.HasStartsBatchAsync(candidates.Select(r => r.Scope).ToList(), date, ct);
            candidates = candidates.Where(r => withStarts.Contains(r.Service.Id)).ToList();
        }
        var ordered = BathsCatalogRules.Order(candidates);
        var size = BathsCatalogRules.ClampPageSize(pageSize);
        var number = Math.Max(1, page ?? 1);
        var items = ordered.Skip((number - 1) * size).Take(size).Select(ToCard).ToList();
        var empty = ordered.Count == 0 ? (filterByDate ? BathsCatalogRules.EmptyDateText : BathsCatalogRules.EmptyCityText) : null;
        return (null, new BathsCatalogPageDto(items, ordered.Count, number, size, city?.Id, city?.Name, filterByDate ? date : null, filterByDate, empty));
    }

    public async Task<BathsCatalogCitiesDto> CitiesAsync(CancellationToken ct)
    {
        var all = await LoadBaseAsync(ct);
        var items = all.GroupBy(r => r.CityId)
            .Select(g => new BathsCatalogCityDto(g.Key, g.First().CityName, g.First().Region, g.Count()))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id).ToList();
        return new BathsCatalogCitiesDto(items);
    }

    // ── the complex page ──

    public async Task<BathsPublicCompanyDto?> CompanyPageAsync(string slug, CancellationToken ct)
    {
        var normalized = BathsSlugPolicy.Normalize(slug);
        var company = await db.Companies.AsNoTracking().Include(c => c.City).FirstOrDefaultAsync(c => c.Slug.ToLower() == normalized && c.Kind == CompanyKind.Baths, ct);
        if (company is null || !company.IsActive) return null;
        var settings = await companyService.LoadSettingsAsync(company.Id, ct: ct);
        var services = await db.StayServices.AsNoTracking().Where(s => s.CompanyId == company.Id && s.IsPublished && s.ArchivedAtUtc == null)
            .OrderBy(s => s.Position).ThenBy(s => s.Name).ToListAsync(ct);
        var resources = (await ShapeAsync(services.Select(s => new ServiceScope(s, company, settings)).ToList(), ct)).Select(ToCard).ToList();
        var photos = (await db.CompanyPhotos.AsNoTracking().Where(p => p.CompanyId == company.Id).OrderBy(p => p.Position).ToListAsync(ct))
            .Select(p => new ServicePhotoDto(p.Id, p.Url, string.IsNullOrEmpty(p.ThumbnailUrl) ? p.Url : p.ThumbnailUrl, p.Position)).ToList();
        var gate = await companyService.EvaluateGateAsync(company, settings, ct);
        return new BathsPublicCompanyDto(company.Slug, company.Name, company.Description, company.Address, company.City?.Name ?? string.Empty, company.TimeZoneId, company.Phone,
            company.LogoUrl, company.YandexMapsUrl, company.TwoGisUrl, photos, StaysCompanyService.ProviderPublic(settings), gate.Accepting,
            gate.Accepting ? null : ServiceTexts.NotAcceptingGuest, resources);
    }
}
