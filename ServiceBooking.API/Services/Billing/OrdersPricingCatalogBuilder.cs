using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Pure mapping from "Заказы" tariff rows to <see cref="OrdersPublicPricingDto"/> (ARCHITECTURE_CYCLE37.md §37.9.3) — no database, no cache.
/// No options (the Orders line has none to sell), no subscriber counts, no internal flags.
/// </summary>
public static class OrdersPricingCatalogBuilder
{
    public const string DefaultNotice =
        "Подключение тарифа выполняет администратор платформы — оставьте заявку в кабинете, и мы свяжемся с вами.";

    public static OrdersPublicPricingDto Build(
        string version,
        IEnumerable<SubscriptionPlanConfig> plans,
        int productsCeiling,
        string? legalNotice,
        string notice = DefaultNotice)
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
                IncludedProductsPerShop: Math.Min(p.MaxProductsPerShop ?? productsCeiling, productsCeiling),
                IncludedOrdersPerMonth: p.MaxOrdersPerMonth,
                SortOrder: p.SortOrder,
                IsFree: p.IsSystemFree))
            .ToList();

        return new OrdersPublicPricingDto(
            Version: version,
            Currency: "RUB",
            Plans: publicPlans,
            Notice: notice,
            LegalNotice: string.IsNullOrWhiteSpace(legalNotice) ? null : legalNotice);
    }
}
