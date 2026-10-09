using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Baths;

/// <summary>
/// API_CONTRACT_CYCLE42.md §42.29 — the tariff check of publishing a «Бани» resource (402). Pure <see cref="Check"/> + the loading of the subscription and of the count of
/// published resources of the account (all «Бани» companies). Must run under the lock <c>billing-account:{accountId}</c>.
////// </summary>
public class BathsPublishGate(StaysPlanResolver plans, IStaysClock clock)
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
        var plan = await plans.GetForAccountAsync(SlotVerticals.Baths, accountId, clock.UtcNow, ct);
        var published = plan.HasActivePlan ? await plans.CountPublishedResourcesAsync(accountId, ct) : 0;
        return Check(plan.HasActivePlan, plan.PlanName, plan.MaxResources, published);
    }
}
