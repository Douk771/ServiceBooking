using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// API_CONTRACT_CYCLE37.md §37.22.4, §37.22.6 — the public house page and the calendar of nights. Neither ever contains the requisites for payment (Т37-04), a guest's
/// name or phone, or the reason a night is taken: only the state of the night.
/// </summary>
public class StaysHousePageService(AppDbContext db, StaysCompanyService companyService, IStaysClock clock)
{
    public const string BadPeriodText = "Неверный период календаря";

    public async Task<PublicHouseDto?> HouseAsync(string companySlug, string houseSlug, CancellationToken ct)
    {
        var normalized = StaysSlugPolicy.Normalize(companySlug);
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug.ToLower() == normalized && c.Kind == CompanyKind.Stays, ct);
        if (company is null) return null;
        var slug = StaysSlugPolicy.Normalize(houseSlug);
        var house = await db.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.CompanyId == company.Id && h.Slug == slug, ct);
        // Not published and not archived (a draft) is indistinguishable from "does not exist".
        if (house is null || (!house.IsPublished && house.ArchivedAtUtc == null)) return null;

        var settings = await companyService.LoadSettingsAsync(company.Id, ct: ct);
        var photos = await db.HousePhotos.AsNoTracking().Where(p => p.HouseId == house.Id).OrderBy(p => p.Position).ToListAsync(ct);
        var now = clock.UtcNow;
        var today = StayTime.LocalDate(company.TimeZoneId, now);
        var periods = house.PriceMode == HousePriceMode.ByDates
            ? HouseService.ToValues(await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == house.Id && p.EndDate >= today).ToListAsync(ct)) : [];
        var gate = await companyService.EvaluateGateAsync(company, settings, ct);
        var available = house.ArchivedAtUtc == null && company.IsActive;

        return new PublicHouseDto(
            house.Id, house.Slug, house.Name, house.Description, photos.Select(p => new HousePhotoDto(p.Id, p.Url, p.ThumbnailUrl, p.Position)).ToList(),
            house.Capacity, new ExtraBedsDto(house.ExtraBedsEnabled, house.ExtraBedsMax, house.ExtraBedPriceRub), house.DogsForbidden, settings.DogFeeRub,
            house.HasCot, settings.CotFeeRub, HouseService.AmenityDtos(house.AmenitiesMask), house.Address ?? company.Address,
            house.YandexMapsUrl ?? company.YandexMapsUrl, house.TwoGisUrl ?? company.TwoGisUrl,
            house.ObjectKind is { } kind ? new PublicRegistryDto(kind, StaysTexts.ObjectKindLabel(kind), house.RegistryNumber, house.RegistryUrl) : null,
            new PublicCompanyRefDto(company.Slug, company.Name, company.Phone, company.LogoUrl, $"/{company.Slug}"),
            StaysCompanyService.ProviderPublic(settings),
            new PublicStayRulesDto(StayFormat.Time(settings.CheckInTime), StayFormat.Time(settings.CheckOutTime), settings.MinNights, settings.MaxNights, settings.HorizonDays,
                settings.AllowGapFill, settings.AllowSameDayCheckIn, settings.HoldMinutes, settings.PrepayPercent, settings.CancellationPolicy,
                StaysTexts.CancellationSummary(settings.CancellationPolicy)),
            HousePricing.PriceFrom(house.PriceMode, house.ConstantPriceRub, periods, today),
            available && gate.Accepting, available && gate.Accepting ? null : StaysTexts.NotAcceptingGuest,
            available, available ? null : StaysTexts.HouseUnavailable, today, company.TimeZoneId);
    }

    public async Task<(ActionResult? Error, HouseCalendarDto? Calendar)> CalendarAsync(Guid houseId, string? fromRaw, string? toRaw, CancellationToken ct)
    {
        var house = await db.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.Id == houseId && h.IsPublished && h.ArchivedAtUtc == null, ct);
        var company = house is null ? null : await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == house.CompanyId && c.Kind == CompanyKind.Stays, ct);
        if (house is null || company is null) return (new NotFoundResult(), null);

        var settings = await companyService.LoadSettingsAsync(company.Id, ct: ct);
        var now = clock.UtcNow;
        var today = StayTime.LocalDate(company.TimeZoneId, now);
        var lastNight = StayRules.LastBookableNight(today, settings.HorizonDays);

        var from = today;
        var to = lastNight.AddDays(1);
        if (!string.IsNullOrWhiteSpace(fromRaw) && !StaysCatalogService.TryDate(fromRaw, out from)) return (new BadRequestObjectResult(BadPeriodText), null);
        if (!string.IsNullOrWhiteSpace(toRaw))
        {
            if (!StaysCatalogService.TryDate(toRaw, out to)) return (new BadRequestObjectResult(BadPeriodText), null);
        }
        else if (!string.IsNullOrWhiteSpace(fromRaw)) to = from.AddDays(Math.Min(400, settings.HorizonDays));
        if (to <= from || to.DayNumber - from.DayNumber > 400) return (new BadRequestObjectResult(BadPeriodText), null);

        var periods = house.PriceMode == HousePriceMode.ByDates
            ? HouseService.ToValues(await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == house.Id && p.EndDate >= from && p.StartDate < to).ToListAsync(ct)) : [];
        var occupied = await db.HouseOccupancies.AsNoTracking()
            .Where(o => o.HouseId == house.Id && o.ReleasedAtUtc == null && o.StartDate < to && o.EndDate > from)
            .Select(o => new { o.StartDate, o.EndDate, o.HoldExpiresAtUtc }).ToListAsync(ct);

        var days = new List<CalendarDayDto>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var price = HousePricing.PriceFor(house.PriceMode, house.ConstantPriceRub, periods, d);
            if (d < today || d > lastNight || price is null) { days.Add(new CalendarDayDto(d, CalendarDayState.Unavailable, price)); continue; }
            var cover = occupied.Where(o => o.StartDate <= d && d < o.EndDate && (o.HoldExpiresAtUtc is null || o.HoldExpiresAtUtc > now)).ToList();
            var state = cover.Count == 0 ? CalendarDayState.Free
                : cover.Any(o => o.HoldExpiresAtUtc is null) ? CalendarDayState.Occupied : CalendarDayState.MayFreeUp;
            days.Add(new CalendarDayDto(d, state, price));
        }
        return (null, new HouseCalendarDto(house.Id, today, from, to, settings.MinNights, settings.MaxNights, settings.AllowGapFill, settings.AllowSameDayCheckIn, lastNight, days));
    }
}
