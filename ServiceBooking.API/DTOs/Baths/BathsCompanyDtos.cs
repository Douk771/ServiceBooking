using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;

namespace ServiceBooking.API.DTOs.Baths;

// API_CONTRACT_CYCLE42.md §42.27, §42.28, §42.33 — the company, its cabinet card, the settings, the closed schedule of the bath attendant.

public record BathsCompanyCreateInput(
    string? Name, string? Slug, int? CityId, string? Address, string? Phone, string? Description, string? OwnerTermsVersion, string? TrialTermsVersion);

public record BathsSettingsDto(
    int HorizonDays, int HoldMinutes, bool HousekeeperSeesGuestComment, bool ShowInCatalog, bool SessionReminderEnabled, int SessionReminderHours);

public record BathsPlanSummaryDto(
    string? PlanName, bool IsTrial, DateTime? PaidUntilUtc, int? MaxResources, int ResourcesPublished, string WarningLevel, string? Text);

public record BathsCompanyManageDto(
    Guid Id, string Name, string Slug, string? Description, string? Phone, string? Email, string? LogoUrl, string? Address,
    string? YandexMapsUrl, string? TwoGisUrl, int? CityId, string CityName, string TimeZoneId, bool TimeZoneIsManual, bool IsActive, bool ShowInCatalog,
    string PublicUrl, StaysMyRole MyRole, List<StaysPermission> MyPermissions, BathsSettingsDto? Settings, PaymentDetailsDto? PaymentDetails,
    ProviderFullDto? Provider, GateDto Gate, List<ChecklistItemDto> Checklist, BathsPlanSummaryDto Plan, int? AwaitingPaymentCount);

public record BathsCompanyCreatedDto(BathsCompanyManageDto Company, string Token, StaysTrialOutcomeDto? Trial);

public record BathsCompanyListItemDto(
    Guid Id, string Name, string Slug, string? LogoUrl, string PublicUrl, StaysMyRole MyRole, bool AcceptingBookings, int? AwaitingPaymentCount);

public record BathsSlugCheckDto(string Suggested, bool Available, StaysConflictDto? Conflict);

public record BathsRevisionDto(long Revision);

/// <summary>The bath attendant's schedule. The shape is CLOSED: no phone, amounts, files, requisites, journal or payment status (only the mark that payment is not confirmed).</summary>
public record BathScheduleSessionDto(
    Guid SessionId, string ServiceName, string TimeLabel, string PreparedUntilLabel, string? GuestName, int? GuestsCount, List<ScheduleItemDto> Items,
    string? Comment, bool PaymentUnconfirmed);

public record BathScheduleDayDto(DateOnly Date, string Label, List<BathScheduleSessionDto> Sessions);

public record BathScheduleDto(DateOnly Today, List<BathScheduleDayDto> Days);
