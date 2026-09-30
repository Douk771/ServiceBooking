using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.DTOs.Catalog;

// API_CONTRACT_CYCLE25.md §531–§532. Enums are strings in JSON.

public sealed record CatalogCityDto(int Id, string Name, string Region, string Label);

public sealed record GoodsCatalogShopDto(
    string Slug, string Path, string Name, string? Address, string CityName, ServiceBooking.API.DTOs.Shops.ShopOpenStateDto OpenState,
    CatalogAcceptance Acceptance, string AcceptanceText);

public sealed record GoodsCatalogPageDto(
    List<GoodsCatalogShopDto> Items, int Page, int PageSize, int TotalCount, CatalogCityDto? City, string? EmptyText);

public sealed record CatalogListingCheckDto(CatalogListingCheckCode Code, string Text, bool Done);

public sealed record CatalogListingDto(
    bool ShowInCatalog, bool AllowedByPlan, bool Visible, string StatusText, string? NotAllowedByPlanText, List<CatalogListingCheckDto> Checklist);

public sealed record CatalogListingInputDto(bool? ShowInCatalog);
