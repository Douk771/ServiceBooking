using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.2.3, §39.14, API_CONTRACT_CYCLE39.md §39.22, §39.26 — reading and shaping services: the public page (price table in the guest's words, no requisites, no occupied
/// intervals, no tourist tax), the blocks on the company and house pages, and the cabinet DTOs (list, card, price rules with the 7×24 matrix, positions).
/// </summary>
public class ServiceCatalogService(AppDbContext db, ServiceSlotService slots, StaysCompanyService companyService, PublicSiteLinks links, IOptions<StaysOptions> options, IStaysClock clock)
{
    private static readonly string[] IsoDayNames = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

    public static string DayLabel(int isoDay) => ServiceTimeFormat.IsoDayName(isoDay);

    // ── cabinet ──

    public async Task<List<StaysServiceConflictCode>> PublishProblemsAsync(StayService service, Company company, StaysSettings settings, CancellationToken ct)
    {
        var hasRules = await db.StayServicePriceRules.AsNoTracking().AnyAsync(r => r.ServiceId == service.Id, ct);
        var today = slots.TodayOf(company);
        var hasWindows = await db.StayServiceWeeklyWindows.AsNoTracking().AnyAsync(w => w.ServiceId == service.Id, ct)
            || (await db.StayServiceDateOverrides.AsNoTracking().Where(o => o.ServiceId == service.Id && !o.IsClosed && o.BusinessDate >= today && o.BusinessDate <= today.AddDays(settings.HorizonDays)).CountAsync(ct)) > 0;
        return ServicePublishRules.Problems(service.ArchivedAtUtc != null, hasRules, hasWindows, Slots.SlotVerticals.Find(company.Kind)?.RequiresCapacityToPublish ?? false, service.Capacity is not null).ToList();
    }

    public async Task<List<ServiceListItemDto>> ListAsync(Company company, StaysSettings settings, CancellationToken ct)
    {
        var services = await db.StayServices.AsNoTracking().Where(s => s.CompanyId == company.Id).OrderBy(s => s.ArchivedAtUtc != null).ThenBy(s => s.Position).ThenBy(s => s.Name).ToListAsync(ct);
        var ids = services.Select(s => s.Id).ToList();
        var covers = (await db.StayServicePhotos.AsNoTracking().Where(p => ids.Contains(p.ServiceId)).OrderBy(p => p.Position).ToListAsync(ct))
            .GroupBy(p => p.ServiceId).ToDictionary(g => g.Key, g => g.First());
        var withSessions = (await db.StayServiceSessions.AsNoTracking().Where(s => ids.Contains(s.ServiceId)).Select(s => s.ServiceId).Distinct().ToListAsync(ct)).ToHashSet();
        var result = new List<ServiceListItemDto>();
        foreach (var s in services)
        {
            var problems = (await PublishProblemsAsync(s, company, settings, ct)).Select(p => p.ToString()).ToList();
            result.Add(new ServiceListItemDto(s.Id, s.Slug, s.Name, s.IsPublished, s.ArchivedAtUtc != null, s.Position, covers.GetValueOrDefault(s.Id) is { } c ? c.ThumbnailUrl ?? c.Url : null,
                problems, withSessions.Contains(s.Id)));
        }
        return result;
    }

    public async Task<ServiceManageDto> BuildManageAsync(Company company, StayService s, StaysSettings settings, CancellationToken ct)
    {
        var photos = await db.StayServicePhotos.AsNoTracking().Where(p => p.ServiceId == s.Id).OrderBy(p => p.Position).ToListAsync(ct);
        var problems = (await PublishProblemsAsync(s, company, settings, ct)).Select(p => p.ToString()).ToList();
        var has = await db.StayServiceSessions.AsNoTracking().AnyAsync(x => x.ServiceId == s.Id, ct);
        var b = options.Value.Services.CancellationBoundaryHours;
        return new ServiceManageDto(s.Id, s.Slug, s.Name, s.Description, s.MinHours, s.MaxHours, s.StepMinutes, s.BufferMinutes, s.ShowBufferToGuests, s.MinLeadMinutes,
            s.StandalonePrepayPercent, s.CancellationPolicy, s.CancellationBoundaryHours, new BoundaryRangeDto(b.Min, b.Max), s.AvailableForHouseBookings, s.IsPublished,
            s.ArchivedAtUtc != null, s.Position, photos.Select(PhotoDto).ToList(), Slots.SlotVerticals.IsSlotKind(company.Kind) ? links.ResourcePageUrl(company.Kind, company.Slug, s.Slug) : links.ServicePageUrl(company.Slug, s.Slug), problems, has,
            s.Capacity, Slots.OwnerTextChecks.Check(s.Description).Warnings.ToList());
    }

    public static ServicePhotoDto PhotoDto(StayServicePhoto p) => new(p.Id, p.Url, p.ThumbnailUrl ?? p.Url, p.Position);

    public static PriceRuleDto RuleDto(StayServicePriceRule r) => new(r.Id, r.DaysMask, r.FromHour, r.ToHour, r.PriceRub, ServiceTimeFormat.RuleStaff(r.DaysMask, r.FromHour, r.ToHour));

    public async Task<PriceRulesDto> PriceRulesAsync(StayService service, CancellationToken ct)
    {
        var rules = await db.StayServicePriceRules.AsNoTracking().Where(r => r.ServiceId == service.Id).OrderBy(r => r.DaysMask).ThenBy(r => r.FromHour).ToListAsync(ct);
        var weekly = (await db.StayServiceWeeklyWindows.AsNoTracking().Where(w => w.ServiceId == service.Id).ToListAsync(ct)).ToLookup(w => w.DayOfWeek);
        var first = options.Value.Services.BusinessDayStartMinute / 60;
        var matrix = new List<PriceMatrixRowDto>();
        for (var day = 1; day <= 7; day++)
        {
            var bit = 1 << (day - 1);
            var cells = new List<PriceMatrixCellDto>();
            for (var hour = first; hour < first + 24; hour++)
            {
                var rule = rules.FirstOrDefault(r => (r.DaysMask & bit) != 0 && hour >= r.FromHour && hour < r.ToHour);
                var inWindow = weekly[day].Any(w => w.StartMinute < (hour + 1) * 60 && hour * 60 < w.EndMinute);
                cells.Add(new PriceMatrixCellDto(hour, ServiceTimeFormat.HourLabel(hour), rule?.PriceRub, rule is null && inWindow));
            }
            matrix.Add(new PriceMatrixRowDto(day, DayLabel(day), cells));
        }
        return new PriceRulesDto(rules.Select(RuleDto).ToList(), matrix);
    }

    // ── the public page ──

    public async Task<PublicServiceDto?> PageAsync(CompanyKind kind, string companySlug, string serviceSlug, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == companySlug && c.Kind == kind, ct);
        if (company is null) return null;
        var s = await db.StayServices.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.Slug == serviceSlug, ct);
        if (s is null || (!s.IsPublished && s.ArchivedAtUtc == null)) return null;
        var settings = await companyService.LoadSettingsAsync(company.Id, ct: ct);

        var gate = await companyService.EvaluateGateAsync(company, settings, s.StandalonePrepayPercent ?? 0, ct);
        var available = s.ArchivedAtUtc == null && company.IsActive;
        var photos = await db.StayServicePhotos.AsNoTracking().Where(p => p.ServiceId == s.Id).OrderBy(p => p.Position).ToListAsync(ct);
        var rules = await db.StayServicePriceRules.AsNoTracking().Where(r => r.ServiceId == s.Id).OrderBy(r => r.DaysMask).ThenBy(r => r.FromHour).ToListAsync(ct);
        var items = await db.StayServiceItems.AsNoTracking().Where(i => i.ServiceId == s.Id && i.IsActive).OrderBy(i => i.Position).ToListAsync(ct);
        var prepay = s.StandalonePrepayPercent;
        var ordering = settings.AcceptServiceOrdersWithoutStay;
        var standalone = new PublicServiceStandaloneDto(ordering, prepay, prepay is null ? null : s.CancellationPolicy, prepay is null ? null : s.CancellationBoundaryHours,
            prepay is null ? null : ServiceTexts.CancellationSummary(s.CancellationPolicy, s.CancellationBoundaryHours), settings.HoldMinutes, ordering ? null : ServiceTexts.NotOrderingText);
        return new PublicServiceDto(s.Id, s.Slug, s.Name, s.Description, photos.Select(PhotoDto).ToList(), s.MinHours, s.MaxHours, s.StepMinutes,
            rules.Select(r => new PriceTableRowDto(ServiceTimeFormat.RuleGuest(r.DaysMask, r.FromHour, r.ToHour), r.PriceRub)).ToList(),
            items.Select(i => new ServiceItemPublicDto(i.Id, i.Name, i.PriceRub, i.MaxPerSession)).ToList(), s.ShowBufferToGuests ? s.BufferMinutes : null, standalone,
            new PublicServiceCompanyDto(company.Slug, company.Name, company.Phone, company.LogoUrl, $"/{company.Slug}"), StaysCompanyService.ProviderPublic(settings),
            gate.Accepting, gate.Accepting ? null : ServiceTexts.NotAcceptingGuest, available, available ? null : ServiceTexts.NotAvailable, slots.TodayOf(company), company.TimeZoneId);
    }

    public async Task<List<PublicServiceSummaryDto>> SummariesAsync(Company company, StaysSettings settings, GateResult gate, CancellationToken ct)
    {
        var services = await db.StayServices.AsNoTracking().Where(s => s.CompanyId == company.Id && s.IsPublished && s.ArchivedAtUtc == null).OrderBy(s => s.Position).ThenBy(s => s.Name).ToListAsync(ct);
        if (services.Count == 0) return [];
        var ids = services.Select(s => s.Id).ToList();
        var covers = (await db.StayServicePhotos.AsNoTracking().Where(p => ids.Contains(p.ServiceId)).OrderBy(p => p.Position).ToListAsync(ct)).GroupBy(p => p.ServiceId).ToDictionary(g => g.Key, g => g.First());
        var minPrice = (await db.StayServicePriceRules.AsNoTracking().Where(r => ids.Contains(r.ServiceId)).Select(r => new { r.ServiceId, r.PriceRub }).ToListAsync(ct))
            .GroupBy(r => r.ServiceId).ToDictionary(g => g.Key, g => g.Min(r => r.PriceRub));
        var itemsBy = (await db.StayServiceItems.AsNoTracking().Where(i => ids.Contains(i.ServiceId) && i.IsActive).OrderBy(i => i.Position).ToListAsync(ct)).ToLookup(i => i.ServiceId);
        return services.Select(s => new PublicServiceSummaryDto(s.Id, s.Slug, s.Name, $"/{company.Slug}/uslugi/{s.Slug}", covers.GetValueOrDefault(s.Id)?.Url, covers.GetValueOrDefault(s.Id) is { } c ? c.ThumbnailUrl ?? c.Url : null,
            minPrice.TryGetValue(s.Id, out var p) ? p : null, s.MinHours, settings.AcceptServiceOrdersWithoutStay && gate.Accepting, s.AvailableForHouseBookings, s.Id,
            itemsBy[s.Id].Select(i => new ServiceItemPublicDto(i.Id, i.Name, i.PriceRub, i.MaxPerSession)).ToList())).ToList();
    }

    public async Task<List<ServiceLinkDto>> ServicesForStayAsync(Company company, CancellationToken ct) =>
        await db.StayServices.AsNoTracking().Where(s => s.CompanyId == company.Id && s.IsPublished && s.ArchivedAtUtc == null && s.AvailableForHouseBookings)
            .OrderBy(s => s.Position).ThenBy(s => s.Name).Select(s => new ServiceLinkDto(s.Name, $"/{company.Slug}/uslugi/{s.Slug}", s.Id)).ToListAsync(ct);

    public DateTime Now => clock.UtcNow;
}
