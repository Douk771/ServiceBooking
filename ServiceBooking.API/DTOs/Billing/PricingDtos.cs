namespace ServiceBooking.API.DTOs.Billing;

/// <summary>API_CONTRACT_CYCLE7.md §38.1, OpenAPI PublicPricingDto — the only anonymous response of
/// cycle 7 (GET /api/pricing, GET /api/admin/pricing/preview).</summary>
public record PublicPricingDto(
    string Version,
    string Currency,
    IReadOnlyList<PublicPlanDto> Plans,
    IReadOnlyList<PublicOptionDto> Options,
    string Notice,
    string? LegalNotice);

public record PublicPlanDto(
    Guid Id,
    string Name,
    string? Description,
    decimal PricePerMonth,
    IReadOnlyList<string> Highlights,
    int? IncludedCompanies,
    int? IncludedEmployees,
    int SortOrder,
    bool IsFree,
    // API_CONTRACT_CYCLE18.md §370 — the public "Пробный период" badge (PlanCard.tsx). Additive,
    // defaulted so any other positional PublicPlanDto(...) construction keeps compiling.
    bool IsTrial = false);

public record PublicOptionDto(
    Guid Id,
    string Name,
    string? Description,
    string Kind,
    decimal PricePerMonth,
    string? UnitName,
    string? UnitPriceText,
    int SortOrder);

/// <summary>ARCHITECTURE_CYCLE38.md §38.9, contracts/cycle38/openapi.yaml OrdersPublicPricingDto — GET /api/pricing/orders.</summary>
public record OrdersPublicPricingDto(
    string Version,
    string Currency,
    IReadOnlyList<OrdersPublicPlanDto> Plans,
    string Notice,
    string? LegalNotice);

public record OrdersPublicPlanDto(
    Guid Id,
    string Name,
    string? Description,
    decimal PricePerMonth,
    IReadOnlyList<string> Highlights,
    int? IncludedShops,
    int? IncludedMembers,
    int IncludedProductsPerShop,
    int? IncludedOrdersPerMonth,
    int SortOrder,
    bool IsFree);
