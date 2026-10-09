using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Baths;

/// <summary>The «Бани» tariff in force for an account. No row / expired / inactive = <see cref="HasActivePlan"/> false (no free tier).</summary>
public sealed record BathsPlanFacts(
    Guid? PlanId, string? PlanName, bool HasActivePlan, bool IsTrial, DateTime? PaidUntilUtc, int? MaxResources, bool WasEverSubscribed)
{
    public static BathsPlanFacts None { get; } = new(null, null, false, false, null, null, false);
}

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.14, API_CONTRACT_CYCLE42.md §42.28 — the company-level read model of the «Бани» vertical: settings (stored in
/// <c>StaysSettings</c>), the booking gate (unit = a published resource), the owner's checklist, the plan banner and the cabinet card.
/// </summary>
public class BathsCompanyService(AppDbContext db, StaysCompanyService stays, PublicSiteLinks links, IStaysClock clock)
{
    public const int DefaultReminderHours = 3;

    // ── settings ──

    public static BathsSettingsDto ToSettingsDto(StaysSettings s, Company company) => new(
        s.HorizonDays, s.HoldMinutes, s.HousekeeperSeesGuestComment, company.ShowInPublicListing, s.ServiceReminderHours is not null,
        s.ServiceReminderHours ?? DefaultReminderHours);

    /// <summary>400 text of an invalid settings form; null = valid (API_CONTRACT_CYCLE42.md §42.28).</summary>
    public static string? ValidateSettings(BathsSettingsDto input)
    {
        if (input.HorizonDays is < 30 or > 730) return "Горизонт бронирования — от 30 до 730 дней";
        if (input.HoldMinutes is < 10 or > 180) return "Время на оплату — от 10 до 180 минут";
        if (input.SessionReminderHours is < 1 or > 24) return "Напоминание — за 1…24 часа до начала";
        return null;
    }

    /// <summary>Full replacement. A switched-off reminder is stored as NULL (the hours come back as the default 3).</summary>
    public static void ApplySettings(StaysSettings settings, Company company, BathsSettingsDto input)
    {
        settings.HorizonDays = input.HorizonDays;
        settings.HoldMinutes = input.HoldMinutes;
        settings.HousekeeperSeesGuestComment = input.HousekeeperSeesGuestComment;
        settings.ServiceReminderHours = input.SessionReminderEnabled ? input.SessionReminderHours : null;
        company.ShowInPublicListing = input.ShowInCatalog;
        // TODO(BE-42-4): invalidate the cache of the catalog base (BathsCatalogService) when it exists — «showInCatalog» changes the visibility.
    }

    // ── plan ──

    // TODO(BE-42-2): replace by the vertical-aware StaysPlanResolver (Baths line) — this reads BathsSubscriptions directly so the cabinet card is honest until then.
    public async Task<BathsPlanFacts> GetPlanAsync(Company company, DateTime nowUtc, CancellationToken ct)
    {
        if (company.BillingAccountId is not { } accountId) return BathsPlanFacts.None;
        var sub = await db.BathsSubscriptions.AsNoTracking().Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
        var plan = sub?.PlanConfig;
        if (sub is null || plan is null) return BathsPlanFacts.None;
        var live = sub.IsActive && (sub.PaidUntil is null || sub.PaidUntil >= nowUtc) && plan is { IsActive: true, Line: CompanyKind.Baths };
        return new BathsPlanFacts(plan.Id, plan.Name, live, plan.Id == BathsPlans.TrialSeedId, sub.PaidUntil, plan.MaxResources, WasEverSubscribed: true);
    }

    /// <summary>Published, non-archived resources of ALL «Бани» companies of the account (the unit of the tariff limit).</summary>
    public Task<int> CountPublishedResourcesAsync(Company company, CancellationToken ct) =>
        company.BillingAccountId is not { } accountId ? Task.FromResult(0)
            : db.StayServices.AsNoTracking().CountAsync(s => s.IsPublished && s.ArchivedAtUtc == null &&
                db.Companies.Any(c => c.Id == s.CompanyId && c.BillingAccountId == accountId && c.Kind == CompanyKind.Baths), ct);

    public static (string Level, string? Text) Warning(BathsPlanFacts plan, int published, DateTime nowUtc)
    {
        if (!plan.WasEverSubscribed)
            return ("NoPlan", "Тариф не выбран. Гости не могут бронировать, пока вы не выберете тариф или не активируете пробный период");
        if (!plan.HasActivePlan)
            return plan.IsTrial
                ? ("Expired", "Пробный период закончился. Гости не могут бронировать, пока вы не выберете тариф")
                : ("NoPlan", "Тариф не выбран или срок оплаты закончился. Гости не могут бронировать, пока вы не выберете тариф");
        if (plan.MaxResources is { } max && published > max)
            return ("OverLimit", $"Опубликовано {published} {StaysTexts.Plural(published, "ресурс", "ресурса", "ресурсов")} при лимите {max}: гости не могут бронировать. Снимите лишние ресурсы с публикации или смените тариф");
        if (plan.IsTrial && plan.PaidUntilUtc is { } until)
        {
            var left = until - nowUtc;
            if (left <= TimeSpan.FromDays(1)) return ("TrialEnding1d", "Пробный период закончится завтра");
            if (left <= TimeSpan.FromDays(3)) return ("TrialEnding3d", $"Пробный период закончится через 3 дня — {StayFormat.Date(DateOnly.FromDateTime(until))}");
        }
        return ("None", null);
    }

    // ── gate ──

    private sealed record PublishedResource(string Name, int? PrepayPercent);

    private Task<List<PublishedResource>> PublishedOfCompanyAsync(Guid companyId, CancellationToken ct) =>
        db.StayServices.AsNoTracking().Where(s => s.CompanyId == companyId && s.IsPublished && s.ArchivedAtUtc == null)
            .OrderBy(s => s.Position).ThenBy(s => s.Id).Select(s => new PublishedResource(s.Name, s.StandalonePrepayPercent)).ToListAsync(ct);

    /// <summary>The prepayment of the company = the maximum of its published resources (§42.34.3); 0 when none has one.</summary>
    private static int MaxPrepay(List<PublishedResource> published) => published.Select(r => r.PrepayPercent ?? 0).DefaultIfEmpty(0).Max();

    public async Task<GateResult> EvaluateGateAsync(Company company, StaysSettings settings, CancellationToken ct = default)
    {
        var plan = await GetPlanAsync(company, clock.UtcNow, ct);
        var accountPublished = await CountPublishedResourcesAsync(company, ct);
        var prepay = MaxPrepay(await PublishedOfCompanyAsync(company.Id, ct));
        return StaysBookingGate.Evaluate(company.IsActive, plan.HasActivePlan, accountPublished, plan.MaxResources, prepay, settings.PaymentDetails,
            StaysCompanyService.ProviderFacts(settings), GateUnit.Resource);
    }

    // ── cards ──

    public async Task<BathsCompanyManageDto> BuildManageAsync(Company company, StaysMyRole role, CancellationToken ct = default)
    {
        var settings = await stays.LoadSettingsAsync(company.Id, ct: ct);
        var permissions = StaysAccess.For(role).ToList();
        var canManage = permissions.Contains(StaysPermission.ManageCompany);
        var cityName = company.CityId is { } cid ? await db.Cities.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var now = clock.UtcNow;
        var plan = await GetPlanAsync(company, now, ct);
        var accountPublished = await CountPublishedResourcesAsync(company, ct);
        var published = await PublishedOfCompanyAsync(company.Id, ct);
        var prepay = MaxPrepay(published);
        var gate = StaysBookingGate.Evaluate(company.IsActive, plan.HasActivePlan, accountPublished, plan.MaxResources, prepay, settings.PaymentDetails,
            StaysCompanyService.ProviderFacts(settings), GateUnit.Resource);
        var (level, text) = Warning(plan, accountPublished, now);
        int? awaiting = role == StaysMyRole.Housekeeper ? null
            : await db.StayServiceOrders.AsNoTracking().CountAsync(o => o.CompanyId == company.Id && o.Status == StayBookingStatus.AwaitingPaymentCheck, ct);

        var prepaid = published.FirstOrDefault(r => (r.PrepayPercent ?? 0) > 0);
        var checklist = new List<ChecklistItemDto>
        {
            new("ProfileFilled", !string.IsNullOrWhiteSpace(company.Name) && !string.IsNullOrWhiteSpace(company.Address) && !string.IsNullOrWhiteSpace(company.Phone),
                "Заполните название, адрес и телефон для гостей"),
            new("PaymentDetails", prepaid is null || !string.IsNullOrWhiteSpace(settings.PaymentDetails),
                prepaid is null ? "Заполните реквизиты для оплаты — у ресурса есть предоплата" : $"Заполните реквизиты для оплаты — у ресурса «{prepaid.Name}» есть предоплата"),
            new("ProviderInfo", StaysBookingGate.ProviderComplete(StaysCompanyService.ProviderFacts(settings)), "Заполните сведения об исполнителе"),
            new("ResourcePublished", published.Count > 0, "Опубликуйте хотя бы одну баню или купель"),
            new("Plan", plan.HasActivePlan, "Выберите тариф или активируйте пробный период"),
        };

        return new BathsCompanyManageDto(
            company.Id, company.Name, company.Slug, company.Description, company.Phone, company.Email, company.LogoUrl, company.Address,
            company.YandexMapsUrl, company.TwoGisUrl, company.CityId, cityName ?? string.Empty, company.TimeZoneId, company.TimeZoneIsManual, company.IsActive,
            company.ShowInPublicListing, links.CompanyPageUrl(company), role, permissions,
            role == StaysMyRole.Housekeeper ? null : ToSettingsDto(settings, company),
            canManage ? new PaymentDetailsDto(settings.PaymentDetails, settings.PaymentPurpose) : null,
            canManage ? StaysCompanyService.ProviderFull(settings) : null,
            new GateDto(gate.Accepting, gate.ReasonCode?.ToString(), gate.ReasonText), checklist,
            new BathsPlanSummaryDto(plan.PlanName, plan.IsTrial, plan.PaidUntilUtc, plan.MaxResources, accountPublished, level, text), awaiting);
    }

    public async Task<List<BathsCompanyListItemDto>> ListMineAsync(string userId, CancellationToken ct)
    {
        var rows = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.UserId == userId && cm.Company.Kind == CompanyKind.Baths)
            .OrderBy(cm => cm.Company.Name).ThenBy(cm => cm.CompanyId)
            .Select(cm => new { cm.Company, cm.Role, cm.StaffPosition }).ToListAsync(ct);
        var result = new List<BathsCompanyListItemDto>();
        foreach (var r in rows)
        {
            var role = StaysAccess.RoleOfMember(r.Role == UserRole.CompanyOwner, r.StaffPosition);
            var gate = await EvaluateGateAsync(r.Company, await stays.LoadSettingsAsync(r.Company.Id, ct: ct), ct);
            int? awaiting = role == StaysMyRole.Housekeeper ? null
                : await db.StayServiceOrders.AsNoTracking().CountAsync(o => o.CompanyId == r.Company.Id && o.Status == StayBookingStatus.AwaitingPaymentCheck, ct);
            result.Add(new BathsCompanyListItemDto(r.Company.Id, r.Company.Name, r.Company.Slug, r.Company.LogoUrl, links.CompanyPageUrl(r.Company), role, gate.Accepting, awaiting));
        }
        return result;
    }
}
