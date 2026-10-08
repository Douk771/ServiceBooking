using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>One bookable house in the catalog base — everything the rules and the card need, with no per-request queries.</summary>
public sealed record CatalogHouse(
    Guid HouseId, Guid CompanyId, string CompanySlug, string CompanyName, string HouseSlug, string HouseName, int Position, string? CoverUrl, string? CoverThumbUrl,
    string? Address, string? RegistryNumber, string TimeZoneId, HouseFacts House, SettingsFacts Settings, IReadOnlyList<PricePeriodValue> Periods)
{
    public string Url => $"/{CompanySlug}/{HouseSlug}";
}

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.11, API_CONTRACT_CYCLE37.md §37.22 — the public catalog and the company page. Only the "base" (published houses of active, listed
/// companies that accept bookings, with their prices) is cached, for 30 s; occupancy is NEVER cached — one indexed query per request — and a booking is always
/// re-checked in the database under the house lock, so the cache cannot produce a double booking. Held dates are occupied; expired holds are free.
/// </summary>
public class StaysCatalogService(AppDbContext db, IMemoryCache cache, IOptions<StaysOptions> options, IStaysClock clock, StaysCompanyService companyService)
{
    private const string BaseKey = "stays:catalog-base";

    public const string BothDatesText = "Укажите обе даты: заезд и выезд";
    public const string BadDateText = "Неверный формат даты";
    public const string OrderDatesText = "Дата выезда должна быть позже даты заезда";
    public const string GuestsText = "Число гостей — от 1 до 60";

    public void InvalidateBase() => cache.Remove(BaseKey);

    // ── the base ──

    private async Task<IReadOnlyList<CatalogHouse>> LoadBaseAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(BaseKey, out IReadOnlyList<CatalogHouse>? cached) && cached is not null) return cached;
        var built = await BuildBaseAsync(ct);
        cache.Set(BaseKey, built, TimeSpan.FromSeconds(Math.Max(1, options.Value.CatalogCacheSeconds)));
        return built;
    }

    private async Task<IReadOnlyList<CatalogHouse>> BuildBaseAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.Houses.AsNoTracking()
            .Where(h => h.IsPublished && h.ArchivedAtUtc == null && h.Company.Kind == CompanyKind.Stays && h.Company.IsActive && h.Company.ShowInPublicListing)
            .Select(h => new { House = h, Company = h.Company }).ToListAsync(ct);
        return await ShapeAsync(rows.Select(r => (r.House, r.Company)).ToList(), onlyAccepting: true, now, ct);
    }

    private async Task<List<CatalogHouse>> ShapeAsync(List<(House House, Company Company)> rows, bool onlyAccepting, DateTime now, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var houseIds = rows.Select(r => r.House.Id).ToList();
        var companyIds = rows.Select(r => r.Company.Id).Distinct().ToList();
        var settings = await db.StaysSettings.AsNoTracking().Where(s => companyIds.Contains(s.CompanyId)).ToDictionaryAsync(s => s.CompanyId, ct);
        var covers = (await db.HousePhotos.AsNoTracking().Where(p => houseIds.Contains(p.HouseId)).OrderBy(p => p.Position).Select(p => new { p.HouseId, p.Url, p.ThumbnailUrl }).ToListAsync(ct))
            .GroupBy(p => p.HouseId).ToDictionary(g => g.Key, g => g.First());
        var byDatesIds = rows.Where(r => r.House.PriceMode == HousePriceMode.ByDates).Select(r => r.House.Id).ToList();
        var cutoff = DateOnly.FromDateTime(now).AddDays(-2);
        var periods = (await db.HousePricePeriods.AsNoTracking().Where(p => byDatesIds.Contains(p.HouseId) && p.EndDate >= cutoff).ToListAsync(ct)).ToLookup(p => p.HouseId);

        // the gate of each company, with the tariff counted per billing ACCOUNT
        var accountIds = rows.Select(r => r.Company.BillingAccountId).Where(a => a != null).Select(a => a!.Value).Distinct().ToList();
        var subs = await db.StaysSubscriptions.AsNoTracking().Include(s => s.PlanConfig).Where(s => accountIds.Contains(s.BillingAccountId)).ToDictionaryAsync(s => s.BillingAccountId, ct);
        var published = await db.Houses.AsNoTracking()
            .Where(h => h.IsPublished && h.ArchivedAtUtc == null && h.Company.Kind == CompanyKind.Stays && h.Company.BillingAccountId != null && accountIds.Contains(h.Company.BillingAccountId.Value))
            .GroupBy(h => h.Company.BillingAccountId!.Value).Select(g => new { Account = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Account, x => x.Count, ct);

        var result = new List<CatalogHouse>();
        foreach (var (house, company) in rows)
        {
            var s = settings.GetValueOrDefault(company.Id) ?? new StaysSettings { CompanyId = company.Id };
            if (onlyAccepting)
            {
                var plan = StaysPlanResolver.Resolve(company.BillingAccountId is { } a ? subs.GetValueOrDefault(a) : null, now);
                var count = company.BillingAccountId is { } acc ? published.GetValueOrDefault(acc) : 0;
                var gate = StaysBookingGate.Evaluate(company.IsActive, plan.HasActivePlan, count, plan.MaxHouses, s.PrepayPercent, s.PaymentDetails, StaysCompanyService.ProviderFacts(s));
                if (!gate.Accepting) continue;
            }
            var cover = covers.GetValueOrDefault(house.Id);
            result.Add(new CatalogHouse(
                house.Id, company.Id, company.Slug, company.Name, house.Slug, house.Name, house.Position, cover?.Url, cover?.ThumbnailUrl,
                house.Address ?? company.Address, house.RegistryNumber, company.TimeZoneId, HouseFacts.Of(house), SettingsFacts.Of(s),
                HouseService.ToValues(periods[house.Id])));
        }
        return result;
    }

    // ── the catalog ──

    public async Task<(ActionResult? Error, StayCatalogPage? Page)> CatalogAsync(
        string? checkInRaw, string? checkOutRaw, int guests, int? maxPricePerNight, int page, int pageSize, CancellationToken ct)
    {
        var hasIn = !string.IsNullOrWhiteSpace(checkInRaw);
        var hasOut = !string.IsNullOrWhiteSpace(checkOutRaw);
        if (hasIn != hasOut) return (new BadRequestObjectResult(BothDatesText), null);
        DateOnly checkIn = default, checkOut = default;
        if (hasIn)
        {
            if (!TryDate(checkInRaw, out checkIn) || !TryDate(checkOutRaw, out checkOut)) return (new BadRequestObjectResult(BadDateText), null);
            if (checkOut <= checkIn) return (new BadRequestObjectResult(OrderDatesText), null);
        }
        if (guests is < 1 or > 60) return (new BadRequestObjectResult(GuestsText), null);
        if (maxPricePerNight is <= 0) return (new BadRequestObjectResult("Цена — больше нуля"), null);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? options.Value.CatalogPageSize : pageSize, 1, 50);

        var now = clock.UtcNow;
        var all = await LoadBaseAsync(ct);
        var items = new List<(StayCatalogItemDto Dto, int Sort)>();
        if (!hasIn)
        {
            foreach (var h in all)
            {
                if (guests > h.House.Capacity + (h.House.ExtraBedsEnabled ? h.House.ExtraBedsMax : 0)) continue;
                var today = StayTime.LocalDate(h.TimeZoneId, now);
                var from = HousePricing.PriceFrom(h.House.PriceMode, h.House.ConstantPriceRub, h.Periods, today);
                if (maxPricePerNight is { } max && (from is null || from > max)) continue;
                items.Add((ToItem(h, from, null, null, null, null), from ?? int.MaxValue));
            }
        }
        else
        {
            var ids = all.Select(h => h.HouseId).ToList();
            var occ = (await db.HouseOccupancies.AsNoTracking()
                .Where(o => ids.Contains(o.HouseId) && o.ReleasedAtUtc == null && o.StartDate <= checkOut && o.EndDate >= checkIn)
                .Select(o => new { o.HouseId, Period = new OccupiedPeriod(o.StartDate, o.EndDate, o.HoldExpiresAtUtc) }).ToListAsync(ct))
                .ToLookup(o => o.HouseId, o => o.Period);
            var input = new StayStayInput(checkIn, checkOut, guests, 0, 0, false);
            foreach (var h in all)
            {
                var today = StayTime.LocalDate(h.TimeZoneId, now);
                var eval = StayEvaluator.Evaluate(h.House, h.Settings, h.Periods, occ[h.HouseId].ToList(), input, today, now, manual: false);
                if (eval.Problems.Count > 0 || eval.Money is null) continue;
                var nightsSum = eval.Nights.Sum(n => n.PriceRub);
                var average = (nightsSum + eval.Nights.Count / 2) / eval.Nights.Count;
                if (maxPricePerNight is { } max && average > max) continue;
                var from = HousePricing.PriceFrom(h.House.PriceMode, h.House.ConstantPriceRub, h.Periods, today);
                items.Add((ToItem(h, from, nightsSum, average, eval.Nights.Count, null), average));
            }
        }
        var ordered = items.OrderBy(i => i.Sort).ThenBy(i => i.Dto.HouseName, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Dto.HouseId).Select(i => i.Dto).ToList();
        return (null, new StayCatalogPage(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ordered.Count, page, pageSize));
    }

    private static StayCatalogItemDto ToItem(CatalogHouse h, int? priceFrom, int? total, int? average, int? nights, bool? availableForDates) => new(
        h.HouseId, h.Url, h.HouseName, h.CompanyName, h.CoverUrl, h.CoverThumbUrl, h.House.Capacity, h.House.ExtraBedsEnabled ? h.House.ExtraBedsMax : 0,
        h.House.DogsForbidden, h.Address, h.RegistryNumber, priceFrom, total, average, nights, availableForDates);

    // ── the company page ──

    public async Task<PublicStaysCompanyDto?> CompanyPageAsync(string slug, string? checkInRaw, string? checkOutRaw, int guests, CancellationToken ct)
    {
        var normalized = StaysSlugPolicy.Normalize(slug);
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug.ToLower() == normalized && c.Kind == CompanyKind.Stays, ct);
        if (company is null) return null;
        var now = clock.UtcNow;
        var today = StayTime.LocalDate(company.TimeZoneId, now);
        if (!company.IsActive)
            return new PublicStaysCompanyDto(company.Id, company.Slug, company.Name, null, null, null, false, StaysTexts.PageUnavailable, false, null, null, [], today);

        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct) ?? new StaysSettings { CompanyId = company.Id };
        var houses = await db.Houses.AsNoTracking().Where(h => h.CompanyId == company.Id && h.IsPublished && h.ArchivedAtUtc == null).OrderBy(h => h.Position).ThenBy(h => h.Name).ToListAsync(ct);
        var shaped = await ShapeAsync(houses.Select(h => (h, company)).ToList(), onlyAccepting: false, now, ct);

        DateOnly checkIn = default, checkOut = default;
        var hasDates = TryDate(checkInRaw, out checkIn) && TryDate(checkOutRaw, out checkOut) && checkOut > checkIn;
        ILookup<Guid, OccupiedPeriod>? occ = null;
        if (hasDates)
        {
            var ids = shaped.Select(h => h.HouseId).ToList();
            occ = (await db.HouseOccupancies.AsNoTracking()
                .Where(o => ids.Contains(o.HouseId) && o.ReleasedAtUtc == null && o.StartDate <= checkOut && o.EndDate >= checkIn)
                .Select(o => new { o.HouseId, Period = new OccupiedPeriod(o.StartDate, o.EndDate, o.HoldExpiresAtUtc) }).ToListAsync(ct)).ToLookup(o => o.HouseId, o => o.Period);
        }
        var items = shaped.Select(h =>
        {
            var from = HousePricing.PriceFrom(h.House.PriceMode, h.House.ConstantPriceRub, h.Periods, today);
            if (!hasDates) return ToItem(h, from, null, null, null, null);
            var eval = StayEvaluator.Evaluate(h.House, h.Settings, h.Periods, occ!.Contains(h.HouseId) ? occ[h.HouseId].ToList() : [],
                new StayStayInput(checkIn, checkOut, Math.Clamp(guests, 1, 60), 0, 0, false), today, now, manual: false);
            if (eval.Problems.Count > 0 || eval.Money is null) return ToItem(h, from, null, null, null, false);
            var sum = eval.Nights.Sum(n => n.PriceRub);
            return ToItem(h, from, sum, (sum + eval.Nights.Count / 2) / eval.Nights.Count, eval.Nights.Count, true);
        }).ToList();

        var gate = await companyService.EvaluateGateAsync(company, settings, ct);
        return new PublicStaysCompanyDto(company.Id, company.Slug, company.Name, company.LogoUrl, company.Description, company.Phone, true, null,
            gate.Accepting, gate.Accepting ? null : StaysTexts.NotAcceptingGuest, StaysCompanyService.ProviderPublic(settings), items, today);
    }

    public static bool TryDate(string? raw, out DateOnly date) =>
        DateOnly.TryParseExact((raw ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
