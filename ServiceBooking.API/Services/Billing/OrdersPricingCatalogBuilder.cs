using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Pure mapping from "Заказы" tariff rows to <see cref="OrdersPublicPricingDto"/> (ARCHITECTURE_CYCLE38.md §38.9.3) — no database, no cache.
/// No options (the Orders line has none to sell), no subscriber counts, no internal flags.
/// </summary>
public static class OrdersPricingCatalogBuilder
{
    /// <summary>Byte-for-byte <c>contracts/cycle48/storefront-pages.json</c> <c>pricing.notice.derivedOrders</c> (CY48-B2);
    /// since «Поправка 31.10.2026 (2)» the same text as <see cref="PricingCatalogBuilder.NoticeTemplate"/>.</summary>
    public const string NoticeTemplate =
        "Подключить платный тариф можно в кабинете после регистрации. Как оплатить и как вернуть деньги — на странице «Оплата и возврат».";

    /// <summary>The notice with any operator requisites substituted (computed, see
    /// <see cref="PricingCatalogBuilder.DefaultNotice"/>).</summary>
    public static string DefaultNotice => OperatorPublicRequisites.Fill(NoticeTemplate);

    public static OrdersPublicPricingDto Build(
        string version,
        IEnumerable<SubscriptionPlanConfig> plans,
        int productsCeiling,
        string? legalNotice,
        string? notice = null)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var publicPlans = plans
            // The hidden service tariff «Демо» is excluded explicitly: an admin could make it public by mistake.
            .Where(p => p.Line == CompanyKind.Orders && p.IsActive && p.IsPublic && !ShowcaseCatalog.IsServicePlan(p.Id))
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.PricePerMonth)
            .Select(p => new OrdersPublicPlanDto(
                Id: p.Id,
                Name: p.Name,
                Description: p.Description,
                PricePerMonth: p.PricePerMonth,
                Highlights: PricingCatalogBuilder.SplitHighlights(p.Highlights),
                IncludedShops: p.MaxCompanies,
                IncludedMembers: p.MaxEmployees,
                // Same rule as OwnerSubscriptionService: the tariff's own limit, never above the technical ceiling; never null.
                IncludedProductsPerShop: Math.Max(1, Math.Min(p.MaxProductsPerShop ?? productsCeiling, productsCeiling)),
                IncludedOrdersPerMonth: p.MaxOrdersPerMonth,
                SortOrder: p.SortOrder,
                IsFree: p.IsSystemFree))
            .ToList();

        return new OrdersPublicPricingDto(
            Version: version,
            Currency: "RUB",
            Plans: publicPlans,
            Notice: notice ?? DefaultNotice,
            LegalNotice: string.IsNullOrWhiteSpace(legalNotice) ? null : legalNotice);
    }
}
