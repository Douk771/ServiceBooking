using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>ARCHITECTURE_CYCLE23.md §409.4 — builds <see cref="ShopManageDto"/> (the cabinet view of a shop) from one place.</summary>
public sealed class ShopManageMapper(
    AppDbContext db, PublicSiteLinks links, PhoneVerificationAvailability phoneVerification)
{
    public async Task<ShopManageDto> BuildAsync(Company shop, ShopRole role, CancellationToken ct = default)
    {
        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings();
        var cityName = shop.CityId is null ? null : await db.Cities.AsNoTracking().Where(c => c.Id == shop.CityId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var productCount = await db.Products.AsNoTracking().CountAsync(p => p.CompanyId == shop.Id && p.DeletedAtUtc == null, ct);
        var gate = ShopOrderingGate.Evaluate(shop, settings, DateTime.UtcNow);

        return new ShopManageDto(
            shop.Id, shop.Name, shop.Slug, shop.Description, shop.LogoUrl, shop.Address, shop.Phone, shop.Email,
            shop.CityId, cityName, shop.TimeZoneId, shop.YandexMapsUrl, shop.TwoGisUrl, shop.IsActive,
            links.CompanyPageUrl(shop), role, ToSettingsDto(settings), ToSellerDto(settings), gate.Accepting, gate.ReasonText,
            phoneVerification.IsAvailable, productCount);
    }

    public static ShopSettingsDto ToSettingsDto(ShopSettings s) => new(
        s.CustomerMode, s.AcceptanceMode, s.AllowCustomerCancel, s.TrackStock, s.CompanyId == Guid.Empty ? null : s.UpdatedAtUtc);

    public static SellerInfoDto ToSellerDto(ShopSettings s) => new(
        s.SellerLegalForm, s.SellerLegalName, s.SellerInn, s.SellerOgrn, s.SellerLegalAddress,
        SellerInfoRequirements.IsComplete(s.SellerLegalForm?.ToString(), s.SellerLegalName, s.SellerInn, s.SellerOgrn, s.SellerLegalAddress),
        SellerInfoRequirements.RequiredFields());

    /// <summary>What the storefront shows: null when no field is filled in.</summary>
    public static PublicSellerInfoDto? ToPublicSeller(ShopSettings s) =>
        s.SellerLegalForm is null && string.IsNullOrWhiteSpace(s.SellerLegalName) && string.IsNullOrWhiteSpace(s.SellerInn) &&
        string.IsNullOrWhiteSpace(s.SellerOgrn) && string.IsNullOrWhiteSpace(s.SellerLegalAddress)
            ? null
            : new PublicSellerInfoDto(s.SellerLegalForm, s.SellerLegalName, s.SellerInn, s.SellerOgrn, s.SellerLegalAddress);
}
