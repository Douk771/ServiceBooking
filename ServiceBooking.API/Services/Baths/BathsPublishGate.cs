using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Baths;

/// <summary>
/// API_CONTRACT_CYCLE42.md §42.29 — the tariff check of publishing a «Бани» resource (402). Pure <see cref="Check"/> + the loading of the subscription and of the count of
/// published resources of the account (all «Бани» companies). Must run under the lock <c>billing-account:{accountId}</c>.
/// TODO BE-42-2: replace the direct read of BathsSubscriptions by the vertical-aware StaysPlanResolver / EvaluateGateAsync once BE-42-2 lands.
/// </summary>
public class BathsPublishGate(AppDbContext db, IStaysClock clock)
{
    public const string NoPlanText = "Выберите тариф или активируйте пробный период, чтобы опубликовать баню";

    public static string LimitText(string planName, int max) =>
        $"Тариф «{planName}» позволяет опубликовать {max} {StaysTexts.Plural(max, "ресурс", "ресурса", "ресурсов")}";

    /// <summary>Null — publishing is allowed; otherwise the 402 text.</summary>
    public static string? Check(bool hasActivePlan, string? planName, int? maxResources, int publishedResources)
    {
        if (!hasActivePlan) return NoPlanText;
        if (maxResources is { } max && publishedResources >= max) return LimitText(planName ?? string.Empty, max);
        return null;
    }

    public async Task<string?> CheckAsync(Company company, CancellationToken ct)
    {
        if (company.BillingAccountId is not { } accountId) return NoPlanText;
        var sub = await db.BathsSubscriptions.AsNoTracking().Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
        var plan = sub?.PlanConfig;
        var live = sub is not null && plan is not null && sub.IsActive && (sub.PaidUntil is null || sub.PaidUntil >= clock.UtcNow)
            && plan is { IsActive: true, Line: CompanyKind.Baths };
        var published = live
            ? await (from s in db.StayServices.AsNoTracking() join c in db.Companies.AsNoTracking() on s.CompanyId equals c.Id
               where s.IsPublished && s.ArchivedAtUtc == null && c.BillingAccountId == accountId && c.Kind == CompanyKind.Baths select s.Id).CountAsync(ct)
            : 0;
        return Check(live, plan?.Name, plan?.MaxResources, published);
    }
}
