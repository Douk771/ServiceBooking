using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Shops;

// ARCHITECTURE_CYCLE23.md §402, API_CONTRACT_CYCLE23.md §409–§411, contracts/cycle23/openapi.yaml (the source of truth for
// the shape). Text fields of INPUT records are nullable on purpose: the controller answers a missing/blank value with
// the contract's own Russian 400 string instead of the generic model-validation sentence.

// ── Shop ─────────────────────────────────────────────────────────────────────────────────────────────────────────

public record CreateShopInput(
    string? Name, string? Slug, int? CityId, string? TimeZoneId, string? Description, string? Address,
    string? Phone, string? Email, ShopOwnerTermsInput? OwnerTerms);

public record ShopOwnerTermsInput(string? Version);

public record CreateShopResponse(ShopManageDto Shop, string Token);

// ReasonCode: serialized by name (CatalogConflictCode); null when the slug is available.
public record SlugCheckDto(string Slug, bool Available, string? Reason, CatalogConflictCode? ReasonCode);

public record ShopListItemDto(Guid Id, string Name, string Slug, string? LogoUrl, bool IsActive, string PublicUrl, ShopRole MyRole);

public record ShopSettingsDto(
    ShopCustomerMode CustomerMode, OrderAcceptanceMode AcceptanceMode, bool AllowCustomerCancel, bool TrackStock,
    DateTime? UpdatedAtUtc);

public record ShopSettingsInput(
    ShopCustomerMode CustomerMode, OrderAcceptanceMode AcceptanceMode, bool AllowCustomerCancel, bool TrackStock);

public record SellerInfoInput(LegalEntityForm? LegalForm, string? LegalName, string? Inn, string? Ogrn, string? LegalAddress);

public record SellerInfoDto(
    LegalEntityForm? LegalForm, string? LegalName, string? Inn, string? Ogrn, string? LegalAddress,
    bool IsComplete, IReadOnlyList<string> RequiredFields);

public record PublicSellerInfoDto(LegalEntityForm? LegalForm, string? LegalName, string? Inn, string? Ogrn, string? LegalAddress);

public record ShopManageDto(
    Guid Id, string Name, string Slug, string? Description, string? LogoUrl, string? Address, string? Phone, string? Email,
    int? CityId, string? CityName, string TimeZoneId, string? YandexMapsUrl, string? TwoGisUrl, bool IsActive,
    string PublicUrl, ShopRole MyRole, ShopSettingsDto Settings, SellerInfoDto Seller, bool AcceptingOrders,
    string? NotAcceptingReason, bool PhoneVerificationAvailable, int ProductCount,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §472).
    ShopNotAcceptingCode? NotAcceptingCode, PickupSettingsDto PickupSettings, bool WorkingHoursSet, ShopAcceptanceDto Acceptance,
    ShopOpenStateDto OpenState, List<SetupChecklistItemDto> SetupChecklist, OrderLimitDto OrderLimit, int ProductLimit,
    // Cycle 26 (API_CONTRACT_CYCLE26.md §562).
    string? CityRegion, int? UtcOffsetMinutes, bool TimeZoneChangeAllowed, string? TimeZoneChangeLockedText);

public record ShopSlugInput(string? Slug);

// ── Catalog ──────────────────────────────────────────────────────────────────────────────────────────────────────

public record CategoryDto(Guid Id, string Name, int Position, bool IsHidden, int ProductCount);

public record CategoryInput(string? Name, bool IsHidden = false);

public record UuidList(List<Guid>? Ids);

public record FoodInfoDto(string? CompositionAndAllergens);

public record StockDto(int? OnHand, int Reserved, int? Free);

public record ProductDto(
    Guid Id, Guid? CategoryId, string Name, string? Description, string? ImageUrl, string? ThumbnailUrl, ProductUnit Unit,
    decimal Price, string? PortionText, int? WeightStepGrams, int MinQuantity, int MaxQuantity, int Position,
    bool IsPublished, bool IsSoldOut, FoodInfoDto FoodInfo, StockDto Stock, bool AvailableToCustomers,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §482.1).
    List<DayOfWeek> AvailableWeekdays, string? WeekdaysLabel, SoldOutDto? SoldOut);

/// <summary><c>AvailableWeekdays</c> null = every day (cycle-23 behavior); an empty list = only through a daily menu.</summary>
public record ProductInput(
    Guid? CategoryId, string? Name, string? Description, ProductUnit Unit, decimal Price, string? PortionText,
    int? WeightStepGrams, int? MinQuantityGrams, bool IsPublished, FoodInfoDto? FoodInfo, List<DayOfWeek>? AvailableWeekdays = null);

public record ProductOrderInput(Guid? CategoryId, List<Guid>? ProductIds);

/// <summary><c>Scope</c> null with <c>IsSoldOut = true</c> = UntilCancelled (cycle-23 compatibility).</summary>
public record SoldOutInput(bool IsSoldOut, SoldOutScope? Scope = null);

public record StockInput(int? OnHand);

// ── Storefront ───────────────────────────────────────────────────────────────────────────────────────────────────

public record StorefrontProductDto(
    Guid Id, string Name, string? Description, string? ImageUrl, string? ThumbnailUrl, ProductUnit Unit, decimal Price,
    string? PortionText, int? WeightStepGrams, int MinQuantity, int MaxQuantity, FoodInfoDto FoodInfo, bool Available,
    StorefrontUnavailableReason? UnavailableReason = null);

public record StorefrontCategoryDto(Guid? Id, string Name, List<StorefrontProductDto> Products);

public record StorefrontDto(
    string Slug, string Name, string PublicUrl, string? LogoUrl, string? Description, string? Address, string? CityName,
    string? Phone, string? YandexMapsUrl, string? TwoGisUrl, bool IsAvailable, bool AcceptingOrders,
    string? NotAcceptingReason, ShopCustomerMode CustomerMode, bool AllowCustomerCancel, PublicSellerInfoDto? Seller,
    List<StorefrontCategoryDto> Categories,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §477.1).
    ShopNotAcceptingCode? NotAcceptingCode, DateOnly Date, string? DateNotice, ShopOpenStateDto OpenState,
    StorefrontWorkingHoursDto WorkingHours, PickupOptionsDto Pickup, StorefrontCustomerNotificationsDto CustomerNotifications,
    // Cycle 26 (API_CONTRACT_CYCLE26.md §561).
    string? Email, List<CompanyPhotoDto> Photos);

public record WorkingHoursSummaryLineDto(string DayLabel, string Text);

public record StorefrontWorkingHoursDto(List<WorkingHoursSummaryLineDto> Lines);

public record StorefrontCustomerNotificationsDto(bool WebPushOffered, bool MessengerOffered);
