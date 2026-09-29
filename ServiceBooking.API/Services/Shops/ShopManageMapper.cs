using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>ARCHITECTURE_CYCLE23.md §409.4 — builds <see cref="ShopManageDto"/> (the cabinet view of a shop) from one place.</summary>
public sealed class ShopManageMapper(
    AppDbContext db, PublicSiteLinks links, PhoneVerificationAvailability phoneVerification, ShopGateLoader gates,
    IOptions<OrdersOptions> options)
{
    public async Task<ShopManageDto> BuildAsync(Company shop, ShopRole role, CancellationToken ct = default)
    {
        var context = await gates.LoadAsync(shop, DateTime.UtcNow, ct);
        return await BuildAsync(shop, role, context, ct);
    }

    /// <summary>The cabinet view of a shop from an already loaded <see cref="ShopGateContext"/> (a route that just changed a setting reloads it once).</summary>
    public async Task<ShopManageDto> BuildAsync(Company shop, ShopRole role, ShopGateContext context, CancellationToken ct = default)
    {
        var settings = context.Settings;
        var gate = context.Gate;
        var cityName = shop.CityId is null ? null : await db.Cities.AsNoTracking().Where(c => c.Id == shop.CityId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var productCount = await db.Products.AsNoTracking().CountAsync(p => p.CompanyId == shop.Id && p.DeletedAtUtc == null, ct);

        return new ShopManageDto(
            shop.Id, shop.Name, shop.Slug, shop.Description, shop.LogoUrl, shop.Address, shop.Phone, shop.Email,
            shop.CityId, cityName, shop.TimeZoneId, shop.YandexMapsUrl, shop.TwoGisUrl, shop.IsActive,
            links.CompanyPageUrl(shop), role, ToSettingsDto(settings, context.SettingsRowExists), ToSellerDto(settings), gate.Accepting,
            gate.OwnerText, phoneVerification.IsAvailable, productCount,
            gate.Code, ShopScheduleMapper.ToDto(ShopOrderingGate.PickupSettingsOf(settings)), context.Schedule.HoursSet,
            ShopScheduleMapper.ToDto(gate.Acceptance), ShopScheduleMapper.ToDto(gate.OpenState),
            ShopScheduleMapper.SetupChecklist(context.Schedule.HoursSet), ShopScheduleMapper.ToDto(gate.OrderLimit),
            EffectiveProductLimit(context.Plan, options.Value));
    }

    /// <summary>The limit of products the shop can really have: the tariff's, but never above the technical ceiling (§459.2).</summary>
    public static int EffectiveProductLimit(OrdersPlan plan, OrdersOptions options) =>
        Math.Min(plan.MaxProductsPerShop ?? int.MaxValue, options.MaxProductsPerShop);

    public static ShopSettingsDto ToSettingsDto(ShopSettings s, bool rowExists = true) => new(
        s.CustomerMode, s.AcceptanceMode, s.AllowCustomerCancel, s.TrackStock, rowExists ? s.UpdatedAtUtc : null);

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
