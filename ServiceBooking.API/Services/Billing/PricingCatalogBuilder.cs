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

    /// <summary>openapi-cycle5.yaml AdminPlanDto/AdminPlanInput.highlights: <c>maxItems: 10</c> — the cap
    /// on how many bullets an admin may SAVE for a plan, shared with
    /// <c>AdminController.SplitHighlights/JoinHighlights</c> and enforced at write time by
    /// <see cref="ValidateHighlights"/> (cycle-07 QA finding #4).</summary>
    public const int MaxHighlights = 10;

    /// <summary>openapi-cycle5.yaml AdminPlanDto/AdminPlanInput.highlights item: <c>maxLength: 120</c> —
    /// enforced at write time by <see cref="ValidateHighlights"/> (cycle-07 QA finding #3). Before this,
    /// nothing stopped an admin from saving a bullet longer than the contract's own documented ceiling.</summary>
    public const int MaxHighlightLength = 120;

    /// <summary>openapi-cycle5.yaml PublicPlanDto.highlights: <c>maxItems: 5</c> — DELIBERATELY separate
    /// from <see cref="MaxHighlights"/>. The storefront shows fewer bullets than an admin may save (10),
    /// so this only ever trims what <see cref="Build"/> displays; it must never be used as the write-time
    /// cap in <c>AdminController</c> or an admin who saved 8 bullets would silently lose 3 with no error
    /// (cycle-07 QA finding #4 — the previous fix only synced the two display-side Take() calls, not the
    /// write-time validation, which is why this constant and <see cref="ValidateHighlights"/> now live in
    /// exactly one place each).</summary>
    public const int PublicMaxHighlights = 5;

    /// <summary>
    /// The one highlights validator for both admin write endpoints (<c>AdminController.CreatePlan</c>/
    /// <c>UpdatePlan</c>) — returns a human-readable error, or null when <paramref name="highlights"/> is
    /// within both the count and per-item length ceilings the contract documents. A missing/empty list is
    /// always valid; "no highlights" is not this validator's problem.
    /// </summary>
    public static string? ValidateHighlights(List<string>? highlights)
    {
        if (highlights is null || highlights.Count == 0) return null;

        if (highlights.Count > MaxHighlights)
            return $"Тариф может иметь не более {MaxHighlights} пунктов преимуществ.";

        var tooLong = highlights.FirstOrDefault(h => h.Length > MaxHighlightLength);
        if (tooLong is not null)
            return $"Пункт преимущества не может быть длиннее {MaxHighlightLength} символов.";

        return null;
    }

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
            .Take(PublicMaxHighlights)
            .ToList();
    }
}
