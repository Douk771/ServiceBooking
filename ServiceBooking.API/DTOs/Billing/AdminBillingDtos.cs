namespace ServiceBooking.API.DTOs.Billing;

// ── Plans (BREAKING fix — options/highlights per contract) ──────────────────────
public record AdminPlanOptionRuleDtoV2(Guid OptionId, string Availability, int? IncludedQuantity);

public record AdminPlanInput(
    string Name, string? Description,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(HighlightsFlexibleConverter))] List<string>? Highlights,
    decimal PricePerMonth,
    int? MaxEmployees, int? MaxCompanies,
    bool AllowOnlineBooking = false, bool AllowMailing = false, bool AllowAnalytics = false,
    bool AllowPublicListing = true, bool AllowOnlinePayment = false,
    int? PhotoQuotaMb = null, Core.Enums.PhotoRetention PhotoRetention = Core.Enums.PhotoRetention.SixMonths,
    int NotifyDaysBefore = 7,
    // NOT non-nullable-with-default like the contract's `isPublic`/`sortOrder` (both `bool`/`int` with a
    // schema `default`) — made nullable here on purpose. The existing admin UI/functional-test helper
    // (ServiceBooking.Tests/Tests/AdminTests.cs's ToUpdateDto) omits these two by sending an explicit
    // JSON `null` for "not specified", the same convention the pre-cycle-7 plan DTO used; binding a
    // JSON `null` into a non-nullable `bool`/`int` throws (400) before the action body even runs. Kept
    // nullable, with the old "apply only if present, else leave the current value" semantics in
    // CreatePlan/UpdatePlan, so this fix doesn't also have to rewrite that test helper (see the cycle-07
    // backend report).
    bool? IsPublic = null, bool IsActive = true, int? SortOrder = null,
    List<AdminPlanOptionRuleDtoV2>? Options = null,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §485.3). Line: set at creation (default "Записи"), a different value on PUT is a 409.
    // For the "Заказы" line MaxEmployees / MaxCompanies read as "Макс. участников" / "Макс. магазинов"; null limits = unlimited.
    Core.Enums.CompanyKind? Line = null, int? MaxProductsPerShop = null, int? MaxOrdersPerMonth = null, bool? AllowOrders = null);

// contracts/cycle7/openapi.yaml's AdminPlanInput has no isSystemFree property (additionalProperties:
// false) — changing which plan is the system free one is a distinct, rarer administrative action from
// an ordinary field edit, so it gets its own route/DTO instead of riding along inside AdminPlanInput
// (cycle-07 code review finding B "isSystemFree removal").
public record SetSystemFreeInput(bool IsSystemFree);
