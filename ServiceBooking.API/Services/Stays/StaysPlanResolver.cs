using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>The "Дома" tariff in force for a billing account. No row / expired / inactive = <see cref="HasActivePlan"/> false (no free tier, Q2).</summary>
public sealed record StaysPlan(
    Guid? PlanId, string? PlanName, bool HasActivePlan, bool IsTrial, DateTime? PaidUntilUtc, int? MaxHouses, bool AllowNotificationChannel, bool WasEverSubscribed)
{
    public static StaysPlan None { get; } = new(null, null, false, false, null, null, false, false);
}

/// <summary>ARCHITECTURE_CYCLE37.md §37.10 — the plan of the line, the count of published houses, and the warning level for the owner's banners.</summary>
public class StaysPlanResolver(AppDbContext db)
{
    public static StaysPlan Resolve(StaysSubscription? sub, DateTime nowUtc)
    {
        var plan = sub?.PlanConfig;
        if (sub is null || plan is null) return StaysPlan.None;
        var live = sub.IsActive && (sub.PaidUntil is null || sub.PaidUntil >= nowUtc) && plan is { IsActive: true, Line: CompanyKind.Stays };
        return new StaysPlan(plan.Id, plan.Name, live, plan.Id == StaysPlans.TrialSeedId, sub.PaidUntil, plan.MaxHouses, plan.AllowNotificationChannel, WasEverSubscribed: true);
    }

    public async Task<StaysPlan> GetForAccountAsync(Guid accountId, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var sub = await db.StaysSubscriptions.AsNoTracking().Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
        return Resolve(sub, nowUtc ?? DateTime.UtcNow);
    }

    public async Task<StaysPlan> GetForCompanyAsync(Company company, DateTime? nowUtc = null, CancellationToken ct = default) =>
        company.BillingAccountId is { } id ? await GetForAccountAsync(id, nowUtc, ct) : StaysPlan.None;

    /// <summary>Published, non-archived houses of ALL "Дома" companies of the account (the unit of the tariff limit).</summary>
    public Task<int> CountPublishedHousesAsync(Guid accountId, CancellationToken ct = default) =>
        db.Houses.AsNoTracking().CountAsync(h => h.IsPublished && h.ArchivedAtUtc == null && h.Company.BillingAccountId == accountId && h.Company.Kind == CompanyKind.Stays, ct);

    public Task<int> CountPublishedHousesForCompanyAsync(Company company, CancellationToken ct = default) =>
        company.BillingAccountId is { } id ? CountPublishedHousesAsync(id, ct) : Task.FromResult(0);

    /// <summary>Banner level and text for the owner (API_CONTRACT_CYCLE37.md §37.32).</summary>
    public static (string Level, string? Text) Warning(StaysPlan plan, int housesPublished, DateTime nowUtc)
    {
        if (!plan.WasEverSubscribed)
            return ("NoPlan", "Тариф не выбран. Гости не могут бронировать, пока вы не выберете тариф или не активируете пробный период");
        if (!plan.HasActivePlan)
            return plan.PlanId == StaysPlans.TrialSeedId || plan.IsTrial
                ? ("Expired", "Пробный период закончился. Гости не могут бронировать, пока вы не выберете тариф")
                : ("NoPlan", "Тариф не выбран или срок оплаты закончился. Гости не могут бронировать, пока вы не выберете тариф");
        if (plan.MaxHouses is { } max && housesPublished > max)
            return ("OverLimit", $"Опубликовано {housesPublished} {StaysTexts.Plural(housesPublished, "дом", "дома", "домов")} при лимите {max}: гости не могут бронировать. Снимите лишние дома с публикации или смените тариф");
        if (plan.IsTrial && plan.PaidUntilUtc is { } until)
        {
            var left = until - nowUtc;
            if (left <= TimeSpan.FromDays(1)) return ("TrialEnding1d", "Пробный период закончится завтра");
            if (left <= TimeSpan.FromDays(3)) return ("TrialEnding3d", $"Пробный период закончится через 3 дня — {StayFormat.Date(DateOnly.FromDateTime(until))}");
        }
        return ("None", null);
    }
}
