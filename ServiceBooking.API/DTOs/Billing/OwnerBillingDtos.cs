namespace ServiceBooking.API.DTOs.Billing;

/// <summary>contracts/openapi-cycle5.yaml OwnerSubscriptionDto — GET /api/billing/subscription
/// (US-65, US-68, US-70). Invariant checked by tests on both sides: TotalMonthlyPrice ==
/// Plan.PricePerMonth + sum(Options[].PricePerMonth). Never mentions "биллинг-аккаунт" (Р8).</summary>
public record OwnerSubscriptionDto(
    string Currency,
    string Status,
    string StatusText,
    SubscribedPlanDto Plan,
    IReadOnlyList<SubscribedOptionDto> Options,
    decimal TotalMonthlyPrice,
    DateTime? PaidUntil,
    int? ExpiresInDays,
    bool IsExpiringSoon,
    SubscriptionUsageDto Usage,
    IReadOnlyList<CoveredCompanyDto> Companies,
    SubscriptionWarningDto? Warning,
    IReadOnlyList<AvailableOptionDto> AvailableOptions,
    SubscriptionRequestDto? PendingRequest,
    bool CanRequestChanges);

public record SubscribedPlanDto(Guid? Id, string Name, string? Description, decimal PricePerMonth, IReadOnlyList<string> Includes);

public record SubscribedOptionDto(
    Guid OptionId, string Name, string? Description, string Kind, string? UnitName,
    int Quantity, decimal PricePerUnit, decimal PricePerMonth,
    string Status, string StatusText, DateTime? EndsAt, bool CanDisable,
    // Admin-only additions (AdminSubscribedOptionDto's allOf) — always null on the owner screen.
    DateTime? PaidUntil = null, int? RequestedQuantity = null, DateTime? RequestedAt = null);

public record SubscriptionUsageDto(
    int CompaniesUsed, int? CompaniesLimit, int EmployeesUsed, int? EmployeesLimit,
    string EmployeesText, string CompaniesText,
    int NumbersPaid, int NumbersRegistered, string NumbersText);

public record CoveredCompanyDto(Guid CompanyId, string CompanyName, int EmployeeCount, bool HasNumber);

public record SubscriptionWarningDto(string Kind, string Text, IReadOnlyList<string> Affected);

public record AvailableOptionDto(
    Guid OptionId, string Name, string? Description, string Kind, string? UnitName,
    decimal? PricePerMonth, int? MaxQuantity, string Availability, string AvailabilityText, bool CanRequest);

// ── Requests (US-70, §49) ───────────────────────────────────────────────────────
public record SubscriptionRequestInputDto(Guid? PlanId, List<RequestedOptionInputDto> Options, string? Comment);

public record RequestedOptionInputDto(Guid OptionId, int Quantity);

public record SubscriptionRequestDto(
    Guid Id, string Status, DateTime CreatedAt, Guid? DesiredPlanId, string? DesiredPlanName,
    decimal EstimatedMonthlyPrice, IReadOnlyList<SubscriptionRequestItemDto> Items, string? Comment);

public record SubscriptionRequestItemDto(Guid OptionId, string Name, int Quantity);

// Line stored inside BillingAccount.RequestedOptionsJson (see BillingAccount's own remarks for why
// there's no separate table this stage).
public record RequestedOptionLine(Guid OptionId, int Quantity);
