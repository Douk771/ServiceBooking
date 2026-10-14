using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// The tariff of a slot line ("Дома" or «Бани») in force for a billing account. No row / expired / inactive = <see cref="HasActivePlan"/> false (no free tier, Q2).
/// <see cref="MaxHouses"/> is set for "Дома", <see cref="MaxResources"/> for «Бани»; <see cref="MaxUnits"/> is whichever the line uses (null = unlimited).
/// </summary>
public sealed record StaysPlan(
    Guid? PlanId, string? PlanName, bool HasActivePlan, bool IsTrial, DateTime? PaidUntilUtc, int? MaxHouses, bool WasEverSubscribed,
    int? MaxResources = null)
{
    public int? MaxUnits => MaxHouses ?? MaxResources;

    public static StaysPlan None { get; } = new(null, null, false, false, null, null, false);
}

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.10, ARCHITECTURE_CYCLE42.md §42.5.3 — the plan of a slot line ("Дома" or «Бани»; neither has a free tier), the count of
/// published units (houses or resources) and the warning level for the owner's banners. The class name stays for minimal churn; the methods without
/// a vertical are the "Дома" wrappers of cycle 37.
/// </summary>
public class StaysPlanResolver(AppDbContext db)
{
    public static StaysPlan Resolve(StaysSubscription? sub, DateTime nowUtc) =>
        Resolve(SlotVerticals.Stays, sub?.PlanConfig, sub is null ? null : (sub.IsActive, sub.PaidUntil), nowUtc);

    public static StaysPlan ResolveBaths(BathsSubscription? sub, DateTime nowUtc) =>
        Resolve(SlotVerticals.Baths, sub?.PlanConfig, sub is null ? null : (sub.IsActive, sub.PaidUntil), nowUtc);

    /// <summary>The subscription row of the vertical's table, resolved: <c>plan.Line</c> must be the vertical's, the trial is the vertical's trial plan.</summary>
    public static StaysPlan Resolve(SlotVertical vertical, SubscriptionPlanConfig? plan, (bool IsActive, DateTime? PaidUntil)? sub, DateTime nowUtc)
    {
        if (sub is not { } row || plan is null) return StaysPlan.None;
        var live = row.IsActive && (row.PaidUntil is null || row.PaidUntil >= nowUtc) && plan.IsActive && plan.Line == vertical.Kind;
        var isStays = vertical.Kind == CompanyKind.Stays;
        return new StaysPlan(plan.Id, plan.Name, live, plan.Id == vertical.TrialPlanId, row.PaidUntil, isStays ? plan.MaxHouses : null,
            WasEverSubscribed: true, MaxResources: isStays ? null : plan.MaxResources);
    }

    public async Task<StaysPlan> GetForAccountAsync(Guid accountId, DateTime? nowUtc = null, CancellationToken ct = default) =>
        await GetForAccountAsync(SlotVerticals.Stays, accountId, nowUtc, ct);

    public async Task<StaysPlan> GetForAccountAsync(SlotVertical vertical, Guid accountId, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (vertical.Kind == CompanyKind.Baths)
        {
            var sub = await db.BathsSubscriptions.AsNoTracking().Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
            return ResolveBaths(sub, now);
        }
        var staysSub = await db.StaysSubscriptions.AsNoTracking().Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
        return Resolve(staysSub, now);
    }

    /// <summary>The plan of the company's own vertical (a company that is not a slot company falls back to "Дома", as before).</summary>
    public async Task<StaysPlan> GetForCompanyAsync(Company company, DateTime? nowUtc = null, CancellationToken ct = default) =>
        company.BillingAccountId is { } id ? await GetForAccountAsync(SlotVerticals.Find(company.Kind) ?? SlotVerticals.Stays, id, nowUtc, ct) : StaysPlan.None;

    /// <summary>Published, non-archived houses of ALL "Дома" companies of the account (the unit of the tariff limit).</summary>
    public Task<int> CountPublishedHousesAsync(Guid accountId, CancellationToken ct = default) =>
        db.Houses.AsNoTracking().CountAsync(h => h.IsPublished && h.ArchivedAtUtc == null && h.Company.BillingAccountId == accountId && h.Company.Kind == CompanyKind.Stays, ct);

    public Task<int> CountPublishedHousesForCompanyAsync(Company company, CancellationToken ct = default) =>
        company.BillingAccountId is { } id ? CountPublishedHousesAsync(id, ct) : Task.FromResult(0);

    /// <summary>Published, non-archived resources (services) of ALL «Бани» companies of the account (the unit of the «Бани» tariff limit).</summary>
    public Task<int> CountPublishedResourcesAsync(Guid accountId, CancellationToken ct = default) =>
        db.StayServices.AsNoTracking().CountAsync(s => s.IsPublished && s.ArchivedAtUtc == null
            && db.Companies.Any(c => c.Id == s.CompanyId && c.BillingAccountId == accountId && c.Kind == CompanyKind.Baths), ct);

    /// <summary>The unit of the vertical's tariff limit: houses for "Дома", resources for «Бани».</summary>
    public Task<int> CountPublishedUnitsAsync(SlotVertical vertical, Guid accountId, CancellationToken ct = default) =>
        vertical.Unit switch
        {
            GateUnit.House => CountPublishedHousesAsync(accountId, ct),
            GateUnit.Resource => CountPublishedResourcesAsync(accountId, ct),
            _ => throw new System.Diagnostics.UnreachableException()
        };

    public Task<int> CountPublishedUnitsForCompanyAsync(Company company, CancellationToken ct = default) =>
        company.BillingAccountId is { } id ? CountPublishedUnitsAsync(SlotVerticals.Find(company.Kind) ?? SlotVerticals.Stays, id, ct) : Task.FromResult(0);

    /// <summary>Banner level and text for the owner (API_CONTRACT_CYCLE37.md §37.32).</summary>
    public static (string Level, string? Text) Warning(StaysPlan plan, int housesPublished, DateTime nowUtc) =>
        Warning(plan, housesPublished, nowUtc, GateUnit.House);

    /// <summary>The same, with the unit of the vertical in the over-limit text (API_CONTRACT_CYCLE42.md §42.28).</summary>
    public static (string Level, string? Text) Warning(StaysPlan plan, int unitsPublished, DateTime nowUtc, GateUnit unit)
    {
        var housesPublished = unitsPublished;
        if (!plan.WasEverSubscribed)
            return ("NoPlan", "Тариф не выбран. Гости не могут бронировать, пока вы не выберете тариф или не активируете пробный период");
        if (!plan.HasActivePlan)
            return plan.PlanId == StaysPlans.TrialSeedId || plan.IsTrial
                ? ("Expired", "Пробный период закончился. Гости не могут бронировать, пока вы не выберете тариф")
                : ("NoPlan", "Тариф не выбран или срок оплаты закончился. Гости не могут бронировать, пока вы не выберете тариф");
        if (plan.MaxUnits is { } max && housesPublished > max)
            return ("OverLimit", unit == GateUnit.Resource
                ? $"Опубликовано {housesPublished} {StaysTexts.Plural(housesPublished, "ресурс", "ресурса", "ресурсов")} при лимите {max}: гости не могут бронировать. Снимите лишние ресурсы с публикации или смените тариф"
                : $"Опубликовано {housesPublished} {StaysTexts.Plural(housesPublished, "дом", "дома", "домов")} при лимите {max}: гости не могут бронировать. Снимите лишние дома с публикации или смените тариф");
        if (plan.IsTrial && plan.PaidUntilUtc is { } until)
        {
            var left = until - nowUtc;
            if (left <= TimeSpan.FromDays(1)) return ("TrialEnding1d", "Пробный период закончится завтра");
            if (left <= TimeSpan.FromDays(3)) return ("TrialEnding3d", $"Пробный период закончится через 3 дня — {StayFormat.Date(DateOnly.FromDateTime(until))}");
        }
        return ("None", null);
    }
}
