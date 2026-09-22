namespace ServiceBooking.API.DTOs.Billing;

// ── Options catalog (US-66) ─────────────────────────────────────────────────────
public record AdminOptionDto(
    Guid Id, string Code, string Name, string? Description, string Kind, string? CapabilityKey,
    bool CapabilityKnown, decimal? PricePerMonth, string Currency, string? UnitName, int? MaxQuantity,
    bool IsPublic, bool IsActive, int SortOrder, int SubscribedAccounts);

public record AdminOptionInput(
    string Code, string Name, string? Description, string Kind, string? CapabilityKey,
    decimal? PricePerMonth, string? UnitName, int? MaxQuantity,
    bool IsPublic = false, bool IsActive = true, int SortOrder = 0);

public record CapabilityDto(string Key, string Kind, string Name);

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
    // JSON `null` for "not specified", the same convention UpdatePlanDto used pre-cycle-5; binding a
    // JSON `null` into a non-nullable `bool`/`int` throws (400) before the action body even runs. Kept
    // nullable, with the old "apply only if present, else leave the current value" semantics in
    // CreatePlan/UpdatePlan, so this fix doesn't also have to rewrite that test helper (see the cycle-07
    // backend report).
    bool? IsPublic = null, bool IsActive = true, int? SortOrder = null,
    List<AdminPlanOptionRuleDtoV2>? Options = null,
    // NOT part of contracts/openapi-cycle5.yaml's AdminPlanInput (which has no isSystemFree property at
    // all) — kept here so the existing ADM-0xx functional tests that still exercise "make this plan the
    // system free plan through PUT /plans/{id}" keep passing (see the cycle-07 backend report for why
    // this is a deliberate, reported deviation rather than a silent one).
    bool? IsSystemFree = null);

// ── Billing accounts (US-67) ────────────────────────────────────────────────────
public record AdminBillingAccountListItemDto(
    Guid Id, string? Name, string OwnerUserId, string OwnerName, string? OwnerPhoneMasked,
    string? PlanName, string Status, DateTime? PaidUntil, decimal TotalMonthlyPrice, string Currency,
    int CompaniesUsed, int? CompaniesLimit, int EmployeesUsed, int? EmployeesLimit,
    int NumbersPaid, int NumbersRegistered, bool HasPendingRequest);

public record AdminBillingAccountDto(
    Guid Id, string? Name, string OwnerUserId, string OwnerName, string? OwnerPhoneMasked,
    string Currency, string Status, bool IsActive, Guid? PlanId, SubscribedPlanDto Plan,
    IReadOnlyList<SubscribedOptionDto> Options, decimal TotalMonthlyPrice, DateTime? PaidUntil,
    int CompaniesUsed, int? CompaniesLimit, int EmployeesUsed, int? EmployeesLimit,
    int NumbersPaid, int NumbersRegistered, int GrandfatheredEmployeeBonus, string? GrandfatheredEmployeeBonusText,
    IReadOnlyList<AdminAccountCompanyDto> Companies, IReadOnlyList<AdminAccountChannelDto> Channels,
    SubscriptionRequestDto? PendingRequest);

public record AdminAccountCompanyDto(Guid CompanyId, string CompanyName, string OwnerUserId, string? OwnerName, int EmployeeCount);

public record AdminAccountChannelDto(Guid ChannelId, string? PhoneMasked, string State, string FundingState, DateTime CreatedAt, int AssignedCompanies);

public record AssignSubscriptionInput(
    Guid? PlanId, bool IsActive, DateOnly? PaidUntil, List<AssignOptionInput> Options,
    decimal? Amount, string? Comment, Guid? RequestId, bool ConfirmLimitOverflow = false);

public record AssignOptionInput(Guid OptionId, int Quantity, DateOnly? PaidUntil);

public record AdminSubscriptionChangeLogDto(
    Guid Id, DateTime ChangedAt, string ChangedByName, string ChangeKind, Guid? CompanyId,
    string? OldPlanName, string? NewPlanName, DateTime? OldPaidUntil, DateTime? NewPaidUntil,
    string? OldOptionsSummary, string? NewOptionsSummary, decimal? Amount, string? Comment);

// ── Subscription requests queue (admin, US-67/US-70) ────────────────────────────
public record AdminSubscriptionRequestDto(
    Guid Id, Guid BillingAccountId, string? AccountName, string RequestedByName, string? RequestedByPhoneMasked,
    DateTime CreatedAt, string Status, string? CurrentPlanName, string? DesiredPlanName,
    IReadOnlyList<SubscriptionRequestItemDto> Items, decimal EstimatedMonthlyPrice, string? Comment, int CompaniesCount);
