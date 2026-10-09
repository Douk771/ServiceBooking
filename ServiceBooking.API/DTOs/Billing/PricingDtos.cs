namespace ServiceBooking.API.DTOs.Billing;

/// <summary>API_CONTRACT_CYCLE7.md §38.1, OpenAPI PublicPricingDto — the only anonymous response of
/// cycle 7 (GET /api/pricing, GET /api/admin/pricing/preview).</summary>
public record PublicPricingDto(
    string Version,
    string Currency,
    IReadOnlyList<PublicPlanDto> Plans,
    IReadOnlyList<PublicOptionDto> Options,
    string Notice,
    string? LegalNotice,
    // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.14, API_CONTRACT_CYCLE40.md §40.32): one line per SELLABLE messenger (a closed one gives none) and the note next to them.
    IReadOnlyList<MessengerAddonDto>? MessengerAddons = null,
    MessengerAddonsNoteDto? MessengerAddonsNote = null);

/// <summary>"+ MAX 490 ₽/мес" — every field is the server's text; the frontend only draws it.</summary>
// Transport is a STRING carrying the enum name ("WhatsApp" / "Max") — the same wire form the global string-enum converter gives, but readable by a plain deserializer (the CompanyDto.Kind convention).
public record MessengerAddonDto(string Transport, string Label, decimal PricePerMonth, string Text, string? Footnote);

public record MessengerAddonsNoteDto(string ConditionsUrl, string ConditionsLabel, string TaxNote);

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
