namespace ServiceBooking.API.DTOs.Billing;

/// <summary>API_CONTRACT_CYCLE5.md §38.1, OpenAPI PublicPricingDto — the only anonymous response of
/// cycle 5 (GET /api/pricing, GET /api/admin/pricing/preview).</summary>
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
    bool IsFree);

public record PublicOptionDto(
    Guid Id,
    string Name,
    string? Description,
    string Kind,
    decimal PricePerMonth,
    string? UnitName,
    string? UnitPriceText,
    int SortOrder);
