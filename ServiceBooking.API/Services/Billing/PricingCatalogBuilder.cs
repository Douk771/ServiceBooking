using System.Globalization;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Pure mapping from catalog rows to <see cref="PublicPricingDto"/> (ARCHITECTURE_CYCLE7.md §48) — no
/// database, no cache, no clock. Kept separate from <see cref="PricingCatalogCache"/> so the filtering/
/// sorting/formatting rules are unit-testable on their own, the way §44.3's capability resolver is.
/// </summary>
public static class PricingCatalogBuilder
{
    /// <summary>Text above the price grid, with {{…}} placeholders for the operator requisites. Byte-for-byte
    /// <c>contracts/cycle48/storefront-pages.json</c> <c>pricing.notice.zapis</c> (CY48-B2); the terms come from
    /// the single source <c>contracts/legal/operator-public.json</c> (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.8.1).</summary>
    public const string NoticeTemplate =
        "Как подключить платный тариф: зарегистрируйтесь и отправьте в кабинете заявку на тариф и опции → мы пришлём счёт {{СРОК_СЧЁТА}} на e-mail или телефон из профиля → оплатите счёт банковским переводом → тариф включится {{СРОК_АКТИВАЦИИ}}. Автопродления нет: каждый следующий месяц оплачивается новым счётом.";

    /// <summary>The notice with the operator's invoice and activation terms substituted. Computed, not a constant: the
    /// terms are values of the source file, not text in code.</summary>
    public static string DefaultNotice => OperatorPublicRequisites.Fill(NoticeTemplate);

    /// <summary>Short tax remark of a messenger line ("Условия · {taxNote}") — a constant, NOT the price-list notice
    /// (ТЛ48-03; <c>storefront-pages.json</c> <c>pricing.messengerTaxNote</c>).</summary>
    public const string MessengerTaxNote = "Без НДС: оператор применяет налог на профессиональный доход";

    /// <summary>contracts/cycle7/openapi.yaml AdminPlanDto/AdminPlanInput.highlights: <c>maxItems: 10</c> — the cap
    /// on how many bullets an admin may SAVE for a plan, shared with
    /// <c>AdminController.SplitHighlights/JoinHighlights</c> and enforced at write time by
    /// <see cref="ValidateHighlights"/> (cycle-07 QA finding #4).</summary>
    public const int MaxHighlights = 10;

    /// <summary>contracts/cycle7/openapi.yaml AdminPlanDto/AdminPlanInput.highlights item: <c>maxLength: 120</c> —
    /// enforced at write time by <see cref="ValidateHighlights"/> (cycle-07 QA finding #3). Before this,
    /// nothing stopped an admin from saving a bullet longer than the contract's own documented ceiling.</summary>
    public const int MaxHighlightLength = 120;

    /// <summary>contracts/cycle7/openapi.yaml PublicPlanDto.highlights: <c>maxItems: 5</c> — DELIBERATELY separate
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
        string? notice = null,
        LegalSnapshot? legalSnapshot = null)
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
            // ARCHITECTURE_CYCLE11.md §102.10 (Q11): an option whose legal precondition isn't met
            // (its required document missing or still a draft) is silently excluded too — from the
            // buyer's side it just looks like the option doesn't exist, not like an error.
            // ARCHITECTURE_CYCLE19.md §386.1/§409 — a retired limit option never shows up on the
            // storefront, no matter its IsActive/IsPublic/PricePerMonth combination.
            .Where(o => o.IsActive && o.IsPublic && o.PricePerMonth is not null
                        && LegalOptionGuards.IsPubliclySellable(o.Code, legalSnapshot)
                        && !RetiredLimitOptions.IsRetired(o)
                        // Cycle 40 (§40.14): the messenger options are drawn as their own price lines (MessengerAddonsProvider), never as a generic option.
                        && !Notifications.Funding.ChannelOptionCodes.All.Contains(o.Code))
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.PricePerMonth)
            .Select(ToOptionDto)
            .ToList();

        return new PublicPricingDto(
            Version: version,
            Currency: "RUB",
            Plans: publicPlans,
            Options: publicOptions,
            Notice: notice ?? DefaultNotice,
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
        IsFree: plan.IsSystemFree,
        IsTrial: plan.IsSystemTrial);

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

    internal static IReadOnlyList<string> SplitHighlights(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(PublicMaxHighlights)
            .ToList();
    }
}
