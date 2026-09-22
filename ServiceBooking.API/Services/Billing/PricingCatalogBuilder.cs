using System.Globalization;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Pure mapping from catalog rows to <see cref="PublicPricingDto"/> (ARCHITECTURE_CYCLE5.md §48) — no
/// database, no cache, no clock. Kept separate from <see cref="PricingCatalogCache"/> so the filtering/
/// sorting/formatting rules are unit-testable on their own, the way §44.3's capability resolver is.
/// </summary>
public static class PricingCatalogBuilder
{
    public const string DefaultNotice =
        "Подключение тарифа и опций выполняет администратор платформы — оставьте заявку, и мы свяжемся с вами.";

    /// <summary>N25 — the one cap on how many highlight bullets survive, shared with
    /// <c>AdminController.SplitHighlights/JoinHighlights</c> so the admin editor and the public storefront
    /// agree on how many bullets a saved plan actually keeps. A previous split (10 here, 5 there) let an
    /// admin save 8 bullets and never understand why the public page only showed 5.</summary>
    public const int MaxHighlights = 10;

    public static PublicPricingDto Build(
        string version,
        IEnumerable<SubscriptionPlanConfig> plans,
        IEnumerable<SubscriptionOption> options,
        string? legalNotice,
        string notice = DefaultNotice)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(options);

        var publicPlans = plans
            .Where(p => p.IsActive && p.IsPublic)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.PricePerMonth)
            .Select(ToPlanDto)
            .ToList();

        var publicOptions = options
            // §48: IsActive && IsPublic && PricePerMonth != null — options without a price or not
            // published never appear, regardless of what a plan's availability rule would say.
            .Where(o => o.IsActive && o.IsPublic && o.PricePerMonth is not null)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.PricePerMonth)
            .Select(ToOptionDto)
            .ToList();

        return new PublicPricingDto(
            Version: version,
            Currency: "RUB",
            Plans: publicPlans,
            Options: publicOptions,
            Notice: notice,
            LegalNotice: string.IsNullOrWhiteSpace(legalNotice) ? null : legalNotice);
    }

    private static PublicPlanDto ToPlanDto(SubscriptionPlanConfig plan) => new(
        Id: plan.Id,
        Name: plan.Name,
        Description: plan.Description,
        PricePerMonth: plan.PricePerMonth,
        Highlights: SplitHighlights(plan.Highlights),
        IncludedCompanies: plan.MaxCompanies,
        IncludedEmployees: plan.MaxEmployees,
        SortOrder: plan.SortOrder,
        IsFree: plan.IsSystemFree);

    private static PublicOptionDto ToOptionDto(SubscriptionOption option) => new(
        Id: option.Id,
        Name: option.Name,
        Description: option.Description,
        Kind: option.Kind == OptionKind.Toggle ? "Toggle" : "Quantity",
        PricePerMonth: option.PricePerMonth!.Value,
        UnitName: option.UnitName,
        UnitPriceText: BuildUnitPriceText(option),
        SortOrder: option.SortOrder);

    // §48: "формулировок без единицы быть не должно" — a Quantity option always gets a phrase (either
    // the admin-authored one, or a generic fallback); a Toggle option (no unit to speak of) gets none.
    private static string? BuildUnitPriceText(SubscriptionOption option)
    {
        if (!string.IsNullOrWhiteSpace(option.UnitPriceText)) return option.UnitPriceText;
        if (option.Kind != OptionKind.Quantity || string.IsNullOrWhiteSpace(option.UnitName)) return null;

        var price = option.PricePerMonth!.Value.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{option.UnitName} — {price} ₽/мес";
    }

    private static IReadOnlyList<string> SplitHighlights(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxHighlights)
            .ToList();
    }
}
