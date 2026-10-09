using System.Text.Json.Serialization;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Baths;

// API_CONTRACT_CYCLE42.md §42.22–§42.26 — the anonymous side of «Бани»: the catalog, the cities, the complex page and «Мои брони».

/// <summary>A resource as a card (the catalog and the complex page): no occupied intervals, names or requisites.</summary>
public record BathResourceCardDto(
    Guid ResourceId, string ResourceSlug, string ResourceName, string CompanySlug, string CompanyName, string? Address, string CityName,
    string? CoverUrl, string? CoverThumbUrl, int? PriceFromRub, int MinHours, int? Capacity, string Url);

public record BathsCatalogPageDto(
    List<BathResourceCardDto> Items, int TotalCount, int Page, int PageSize, int? CityId, string? CityName, DateOnly? Date, bool DateFilterApplied, string? EmptyText);

public record BathsCatalogCityDto(int Id, string Name, string? Region, int ResourcesCount);

public record BathsCatalogCitiesDto(List<BathsCatalogCityDto> Items);

public record BathsPublicCompanyDto(
    string Slug, string Name, string? Description, string? Address, string CityName, string TimeZoneId, string? Phone, string? LogoUrl, string? YandexMapsUrl,
    string? TwoGisUrl, List<ServicePhotoDto> Photos, ProviderPublicDto? Provider, bool AcceptingBookings, string? NotAcceptingText, List<BathResourceCardDto> Resources);

public record MyBathOrderDto(
    string OrderUrl, string CompanyName, string ResourceName, string TimeLabel, string? LocalTimeNote, StayBookingStatus Status, string DisplayStatus, string StatusText,
    int TotalRub, bool IsActive);

public record MyBathOrdersDto(List<MyBathOrderDto> Items);

/// <summary>The reminder shown on the booking page after it was queued (§42.36.4) — filled by BE-42-5; always null before.</summary>
public record SessionReminderViewDto(DateTime SentAtUtc, string Text);
