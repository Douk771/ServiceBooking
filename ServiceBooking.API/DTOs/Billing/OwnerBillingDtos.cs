namespace ServiceBooking.API.DTOs.Billing;

/// <summary>contracts/cycle7/openapi.yaml OwnerSubscriptionDto — GET /api/billing/subscription
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
    bool CanRequestChanges,
    // N10, US-70 — set once when an admin rejects the owner's last request, cleared on the next
    // submission or approval (BillingAccount.LastRejectionReason's own remarks). Additive, defaults to
    // null so this doesn't become a breaking positional-argument change for existing callers.
    RejectedRequestDto? LastRejectedRequest = null,
    // Cycle 18, API_CONTRACT_CYCLE18.md §365 — null ONLY when this account never had a trial and isn't
    // eligible for one. Non-null whenever GET /api/billing/trial itself would answer non-null, so the
    // owner cabinet's "Пробный период" card, the always-visible date (Т2), the un-closable expiry
    // notice (Т3) and the activation button all have a field to read.
    TrialStateDto? Trial = null,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §485.1) — the line the screen is built for and, for "Заказы", the month counter and the plans to ask for [legal L14].
    // For the "Записи" line: Line = Services, Orders = null, AvailablePlans = null (the answer is the cycle-23 one plus these three fields).
    // Line is a STRING carrying the enum name (the CompanyDto.Kind convention): clients that read this DTO without a string-enum converter keep working.
    string Line = nameof(Core.Enums.CompanyKind.Services), OrdersUsageDto? Orders = null,
    IReadOnlyList<AvailablePlanDto>? AvailablePlans = null,
    // Cycle 37 (API_CONTRACT_CYCLE37.md §37.21.4) — the «Дома» block; set only when Line = Stays.
    StaysSubscriptionBlockDto? Stays = null,
    // Cycle 40 (§40.14): the price lines of the messenger options, as on the public price list.
    IReadOnlyList<MessengerAddonDto>? MessengerAddons = null,
    MessengerAddonsNoteDto? MessengerAddonsNote = null);

/// <summary>Published houses against the limit of the «Дома» tariff, the trial and the banner level (the same words as StaysPlanSummaryDto).</summary>
public record StaysSubscriptionBlockDto(int HousesPublished, int? MaxHouses, bool IsTrial, DateTime? TrialEndsAtUtc, string WarningLevel, string? Text);

/// <summary>Orders of the month against the limit of the "Заказы" tariff (ARCHITECTURE_CYCLE24.md §459.6). All texts are the server's.</summary>
public record OrdersUsageDto(
    int OrdersThisMonth, int? OrdersLimit, string MonthLabel, string? Text, ServiceBooking.API.Services.Shops.OrderLimitWarningLevel WarningLevel,
    int? ProductsPerShopLimit, bool AllowOrders);

/// <summary>An active tariff of the "Заказы" line the owner may ask for — shown ONLY to a signed-in owner, never publicly [legal L14].
/// Cycle 37 (API_CONTRACT_CYCLE37.md §37.21.4): the same DTO lists the «Дома» plans; <c>MaxHouses</c> (appended) is their house limit — null means
/// "no limit" for a «Дома» plan; for the other lines the field is OMITTED (the schema of cycle 24 is strict about unknown properties).</summary>
public record AvailablePlanDto(
    Guid PlanId, string Name, decimal PricePerMonth, string? Description, IReadOnlyList<string> Highlights, string LimitsText,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? MaxHouses = null);

public record RejectedRequestDto(string Reason, DateTime RejectedAtUtc);

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
    int NumbersPaid, int NumbersRegistered, string NumbersText,
    // Cycle 18, API_CONTRACT_CYCLE18.md §365 (Д3, "деградация = заморозка") — 0/0/null when within
    // limits. Defaulted so this stays additive for any other positional caller of this record.
    int OverLimitCompanies = 0, int OverLimitEmployees = 0, string? OverLimitText = null);

public record CoveredCompanyDto(Guid CompanyId, string CompanyName, int EmployeeCount, bool HasNumber);

public record SubscriptionWarningDto(string Kind, string Text, IReadOnlyList<string> Affected);

public record AvailableOptionDto(
    Guid OptionId, string Name, string? Description, string Kind, string? UnitName,
    decimal? PricePerMonth, int? MaxQuantity, string Availability, string AvailabilityText, bool CanRequest);

// ── Requests (US-70, §49) ───────────────────────────────────────────────────────
public record SubscriptionRequestInputDto(
    Guid? PlanId, List<RequestedOptionInputDto>? Options = null, string? Comment = null,
    // Cycle 24: which line the request is for; not sent = "Записи".
    Core.Enums.CompanyKind? Line = null);

public record RequestedOptionInputDto(Guid OptionId, int Quantity);

public record SubscriptionRequestDto(
    Guid Id, string Status, DateTime CreatedAt, Guid? DesiredPlanId, string? DesiredPlanName,
    decimal EstimatedMonthlyPrice, IReadOnlyList<SubscriptionRequestItemDto> Items, string? Comment,
    // ARCHITECTURE_CYCLE17.md §307.1, API_CONTRACT_CYCLE17.md §325.1 (US-17-08, C15-7) — additive,
    // appended at the end so every existing positional SubscriptionRequestDto(...) call keeps
    // compiling unchanged (same convention as LastRejectedRequest, §7 cycle 7). Non-null only when
    // ALL THREE hold: the account has an active subscription, its current plan is IsPublic == false
    // (snapshot off sale), and this request asks for a PLAN CHANGE (not options-only). Text is
    // composed server-side by legal-counsel's wording (п. 6.13.15.3 03-terms-owner.html) — the
    // frontend prints it as-is, never sourcing its own placeholder.
    string? IrreversibilityNotice = null,
    // ARCHITECTURE_CYCLE19.md §408/§414 — non-null only when Items contains at least one retired limit
    // option (a request submitted before the cycle 19 rollout); appended at the end, same convention
    // as IrreversibilityNotice above.
    string? RetiredOptionsNotice = null,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §485.1): the line of the request; appended at the end like the two notices above.
    string Line = nameof(Core.Enums.CompanyKind.Services));

public record SubscriptionRequestItemDto(
    Guid OptionId, string Name, int Quantity,
    // ARCHITECTURE_CYCLE19.md §407/§408 — true when OptionId names a retired limit option
    // (RetiredLimitOptions); appended at the end so existing positional constructors keep compiling.
    bool Retired = false);

// Line stored inside BillingAccount.RequestedOptionsJson (see BillingAccount's own remarks for why
// there's no separate table this stage).
public record RequestedOptionLine(Guid OptionId, int Quantity);
