using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.3, §37.11.3, §37.6.6 — reading and shaping houses: the cabinet DTOs, the amenities mask, the "no price" ranges.</summary>
public class HouseService(AppDbContext db, PublicSiteLinks links, IStaysClock clock)
{
    public const int MaxPriceRub = 1_000_000;

    // ── amenities mask ──
    public static long ToMask(IEnumerable<HouseAmenity> amenities) => amenities.Aggregate(0L, (m, a) => m | (1L << (int)a));

    public static List<HouseAmenity> FromMask(long mask) =>
        Enum.GetValues<HouseAmenity>().Where(a => (mask & (1L << (int)a)) != 0).ToList();

    public static List<HouseAmenityDto> AmenityDtos(long mask) =>
        FromMask(mask).Select(a => new HouseAmenityDto(a, StaysTexts.AmenityLabel(a))).ToList();

    public static List<HouseAmenityDto> AllAmenities() =>
        Enum.GetValues<HouseAmenity>().Select(a => new HouseAmenityDto(a, StaysTexts.AmenityLabel(a))).ToList();

    // ── prices ──
    public static List<PricePeriodValue> ToValues(IEnumerable<HousePricePeriod> periods) =>
        periods.Select(p => new PricePeriodValue(p.StartDate, p.EndDate, p.PriceRub)).ToList();

    public static PricePeriodDto ToDto(HousePricePeriod p) => new(p.Id, p.StartDate, p.EndDate, p.PriceRub);

    /// <summary>Does the house have a price a guest could use: a constant one, or (ByDates) a period that has not ended before <paramref name="today"/>.</summary>
    public static bool HasPrice(House house, IEnumerable<HousePricePeriod> periods, DateOnly today) =>
        house.PriceMode == HousePriceMode.Constant ? house.ConstantPriceRub.HasValue : periods.Any(p => p.EndDate >= today);

    /// <summary>The ranges of future dates (up to the horizon) with no price in ByDates mode; empty in Constant mode.</summary>
    public static List<DateRangeDto> UncoveredDates(House house, IReadOnlyList<PricePeriodValue> periods, DateOnly today, int horizonDays)
    {
        var result = new List<DateRangeDto>();
        if (house.PriceMode != HousePriceMode.ByDates) return result;
        DateOnly? start = null;
        DateOnly last = today;
        for (var i = 0; i < horizonDays; i++)
        {
            var d = today.AddDays(i);
            var priced = HousePricing.PriceFor(house.PriceMode, house.ConstantPriceRub, periods, d) is not null;
            if (!priced && start is null) start = d;
            if (priced && start is not null)
            {
                result.Add(new DateRangeDto(start.Value, d.AddDays(-1)));
                start = null;
            }
            last = d;
        }
        if (start is not null) result.Add(new DateRangeDto(start.Value, last));
        return result;
    }

    public DateOnly TodayOf(Company company) => StayTime.LocalDate(company.TimeZoneId, clock.UtcNow);

    public string HouseUrl(Company company, House house) => links.HousePageUrl(company.Slug, house.Slug);

    public async Task<HouseManageDto> BuildManageAsync(Company company, House house, CancellationToken ct = default)
    {
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct) ?? new StaysSettings();
        var periods = await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == house.Id).OrderBy(p => p.StartDate).ToListAsync(ct);
        var photos = await db.HousePhotos.AsNoTracking().Where(p => p.HouseId == house.Id).OrderBy(p => p.Position).ToListAsync(ct);
        var attestation = await db.HouseRegistryAttestations.AsNoTracking().Where(a => a.HouseId == house.Id)
            .OrderByDescending(a => a.AttestedAtUtc).FirstOrDefaultAsync(ct);
        string? attestedBy = null;
        if (attestation is not null)
        {
            var u = await db.Users.AsNoTracking().Where(x => x.Id == attestation.AttestedByUserId).Select(x => new { x.FirstName, x.LastName }).FirstOrDefaultAsync(ct);
            attestedBy = u is null ? "Удалённый пользователь" : $"{u.FirstName} {u.LastName}".Trim();
        }
        var hasBookings = await db.StayBookings.AsNoTracking().AnyAsync(b => b.HouseId == house.Id, ct);
        var today = TodayOf(company);
        var values = ToValues(periods);
        var problems = HousePublishRules.HouseProblems(house.ArchivedAtUtc != null, HasPrice(house, periods, today), house.ObjectKind)
            .Select(p => p.ToString()).ToList();

        return new HouseManageDto(
            house.Id, house.Slug, house.Name, house.Description, house.Capacity,
            new ExtraBedsDto(house.ExtraBedsEnabled, house.ExtraBedsMax, house.ExtraBedPriceRub), house.DogsForbidden, house.HasCot,
            FromMask(house.AmenitiesMask), house.Address, house.YandexMapsUrl, house.TwoGisUrl, house.CheckInInfoText, house.PriceMode, house.ConstantPriceRub,
            new HouseRegistryDto(house.ObjectKind, house.RegistryNumber, house.RegistryUrl,
                attestation is null ? null : new LastAttestationDto(attestation.AttestedAtUtc, attestedBy!, attestation.ObjectKind, attestation.RegistryNumber)),
            new RegistryNoticeDto(StaysTexts.RegistryNoticeVersion(), StaysTexts.RegistryNoticeFallback),
            house.IsPublished, house.ArchivedAtUtc != null, house.Position,
            photos.Select(p => new HousePhotoDto(p.Id, p.Url, p.ThumbnailUrl, p.Position)).ToList(),
            HouseUrl(company, house), UncoveredDates(house, values, today, settings.HorizonDays), problems, hasBookings);
    }

    public async Task<List<HouseListItemDto>> ListAsync(Company company, CancellationToken ct = default)
    {
        var houses = await db.Houses.AsNoTracking().Where(h => h.CompanyId == company.Id).OrderBy(h => h.ArchivedAtUtc != null).ThenBy(h => h.Position).ThenBy(h => h.Name).ToListAsync(ct);
        var ids = houses.Select(h => h.Id).ToList();
        var periods = (await db.HousePricePeriods.AsNoTracking().Where(p => ids.Contains(p.HouseId)).ToListAsync(ct)).ToLookup(p => p.HouseId);
        var covers = (await db.HousePhotos.AsNoTracking().Where(p => ids.Contains(p.HouseId)).OrderBy(p => p.Position).ToListAsync(ct))
            .GroupBy(p => p.HouseId).ToDictionary(g => g.Key, g => g.First());
        var today = TodayOf(company);
        return houses.Select(h =>
        {
            var hp = periods[h.Id].ToList();
            var problems = HousePublishRules.HouseProblems(h.ArchivedAtUtc != null, HasPrice(h, hp, today), h.ObjectKind).Select(p => p.ToString()).ToList();
            return new HouseListItemDto(h.Id, h.Slug, h.Name, covers.GetValueOrDefault(h.Id)?.ThumbnailUrl ?? covers.GetValueOrDefault(h.Id)?.Url, h.Capacity,
                h.IsPublished, h.ArchivedAtUtc != null, h.PriceMode, HousePricing.PriceFrom(h.PriceMode, h.ConstantPriceRub, ToValues(hp), today), h.Position,
                HouseUrl(company, h), problems);
        }).ToList();
    }

    /// <summary>A free house address in the company made from the name (-2, -3 …).</summary>
    public async Task<string> SuggestSlugAsync(Guid companyId, string name, CancellationToken ct = default)
    {
        var baseSlug = Shops.SlugTransliterator.ToBase(name, 50, 2, new HashSet<string>());
        var candidates = Shops.SlugTransliterator.Candidates(baseSlug, () => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()).ToList();
        var taken = StaysSlugPolicy.ReservedHouseSlugs.Concat((await db.Houses.AsNoTracking().Where(h => h.CompanyId == companyId && candidates.Contains(h.Slug)).Select(h => h.Slug).ToListAsync(ct))).ToHashSet();
        return candidates.FirstOrDefault(c => !taken.Contains(c)) ?? candidates[^1];
    }
}
