using ServiceBooking.API.DTOs.Billing;

namespace ServiceBooking.Tests.Infrastructure;

// Deserialization shapes for admin billing responses (cycle 22, ARCHITECTURE_CYCLE22.md §371). The API
// builds these responses as anonymous objects, so the product never used these records — they lived in
// ServiceBooking.API/DTOs/Billing/AdminBillingDtos.cs only for the tests' benefit and moved here in P2.

// ── Options catalog (US-66) ─────────────────────────────────────────────────────
public record AdminOptionDto(
    Guid Id, string Code, string Name, string? Description, string Kind, string? CapabilityKey,
    bool CapabilityKnown, decimal? PricePerMonth, string Currency, string? UnitName, int? MaxQuantity,
    bool IsPublic, bool IsActive, int SortOrder, int SubscribedAccounts);

// ── Billing accounts (US-67) ────────────────────────────────────────────────────
public record AdminBillingAccountListItemDto(
    Guid Id, string? Name, string OwnerUserId, string OwnerName, string? OwnerPhoneMasked,
    string? PlanName, string Status, string StatusText, DateTime? PaidUntil, decimal TotalMonthlyPrice, string Currency,
    int CompaniesUsed, int? CompaniesLimit, int EmployeesUsed, int? EmployeesLimit,
    int NumbersPaid, int NumbersRegistered, bool HasPendingRequest);

public record AdminBillingAccountDto(
    Guid Id, string? Name, string OwnerUserId, string OwnerName, string? OwnerPhoneMasked,
    string Currency, string Status, string StatusText, bool IsActive, Guid? PlanId, SubscribedPlanDto Plan,
    IReadOnlyList<SubscribedOptionDto> Options, decimal TotalMonthlyPrice, DateTime? PaidUntil,
    int CompaniesUsed, int? CompaniesLimit, int EmployeesUsed, int? EmployeesLimit,
    int NumbersPaid, int NumbersRegistered, int GrandfatheredEmployeeBonus, string? GrandfatheredEmployeeBonusText,
    IReadOnlyList<AdminAccountCompanyDto> Companies, IReadOnlyList<AdminAccountChannelDto> Channels,
    SubscriptionRequestDto? PendingRequest);

public record AdminAccountCompanyDto(Guid CompanyId, string CompanyName, string OwnerUserId, string? OwnerName, int EmployeeCount);

public record AdminAccountChannelDto(Guid ChannelId, string? PhoneMasked, string State, string FundingState, DateTime CreatedAt, int AssignedCompanies);
