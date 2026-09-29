using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §393.3, §395, §411–§413 — the public storefront of a shop by its address (<c>goods.ezbook.ru/&lt;slug&gt;</c>):
/// the catalog, the cart check and the checkout. A slug that does not exist or belongs to a salon is a bare 404.
/// </summary>
[ApiController]
[Route("api/storefront/{slug}")]
public class StorefrontController(
    AppDbContext db, StockLedger stockLedger, PublicSiteLinks links, OrderCreationService orderCreation) : ControllerBase
{
    /// <summary>
    /// The storefront in three queries (§393.3): the shop with its city and settings; the visible categories and the published
    /// products; and — only when stock tracking is on — the whole reserve in ONE GROUP BY. No N+1, read-only.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("storefront")]
    public async Task<ActionResult<StorefrontDto>> Get(string slug, CancellationToken ct)
    {
        var normalized = SlugPolicy.Normalize(slug);
        var shop = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == normalized && c.Kind == CompanyKind.Orders, ct);
        if (shop is null) return NotFound();

        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings { CompanyId = shop.Id };
        var cityName = shop.CityId is null ? null : await db.Cities.AsNoTracking().Where(c => c.Id == shop.CityId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var gate = ShopOrderingGate.Evaluate(shop, settings, DateTime.UtcNow);

        StorefrontDto Build(List<StorefrontCategoryDto> categories) => new(
            shop.Slug, shop.Name, links.CompanyPageUrl(shop), shop.LogoUrl, shop.Description, shop.Address, cityName, shop.Phone,
            shop.YandexMapsUrl, shop.TwoGisUrl, IsAvailable: shop.IsActive, gate.Accepting, gate.ReasonText, settings.CustomerMode,
            settings.AllowCustomerCancel, ShopManageMapper.ToPublicSeller(settings), categories);

        // A blocked shop: the page exists but is empty ("Магазин недоступен").
        if (!shop.IsActive) return Ok(Build([]));

        var categories = await db.ProductCategories.AsNoTracking()
            .Where(c => c.CompanyId == shop.Id && !c.IsHidden).OrderBy(c => c.Position).ThenBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .ToListAsync(ct);
        var visibleIds = categories.Select(c => c.Id).ToList();
        // Not published, deleted and hidden-category products are never sent. Sold-out / out-of-stock ones ARE, greyed (available = false).
        var products = await db.Products.AsNoTracking()
            .Where(p => p.CompanyId == shop.Id && p.DeletedAtUtc == null && p.IsPublished &&
                        (p.CategoryId == null || visibleIds.Contains(p.CategoryId.Value)))
            .OrderBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToListAsync(ct);
        var reserved = settings.TrackStock ? await stockLedger.GetReservedAsync(shop.Id, ct: ct) : new Dictionary<Guid, int>();

        StorefrontProductDto Map(Product p, ProductCategory? category) => new(
            p.Id, p.Name, p.Description, p.ImageUrl, p.ThumbnailUrl, p.Unit, p.Price, p.PortionText, p.WeightStepGrams,
            OrderQuantityRules.MinQuantity(p.Unit, p.WeightStepGrams, p.MinQuantityGrams), OrderQuantityRules.MaxQuantity(p.Unit),
            new FoodInfoDto(p.CompositionAndAllergens),
            CatalogAvailability.IsAvailable(p, category, settings.TrackStock, StockLedger.Free(p.StockOnHand, reserved.GetValueOrDefault(p.Id)), gate.Accepting));

        var byCategory = products.ToLookup(p => p.CategoryId);
        var result = new List<StorefrontCategoryDto>();
        foreach (var category in categories)
        {
            var items = byCategory[category.Id].Select(p => Map(p, category)).ToList();
            if (items.Count > 0) result.Add(new StorefrontCategoryDto(category.Id, category.Name, items));
        }
        var uncategorized = byCategory[null].Select(p => Map(p, null)).ToList();
        if (uncategorized.Count > 0) result.Add(new StorefrontCategoryDto(null, "Другое", uncategorized));

        return Ok(Build(result));
    }

    [HttpPost("quote")]
    [EnableRateLimiting("storefront")]
    public async Task<ActionResult<QuoteDto>> Quote(string slug, QuoteInput input, CancellationToken ct)
    {
        var (error, quote) = await orderCreation.QuoteAsync(slug, input, ct);
        return error is not null ? error : Ok(quote);
    }

    /// <summary>201 with a new order; 200 with the existing one when the idempotency key was already used (§413.1 step 5).</summary>
    [HttpPost("orders")]
    [EnableRateLimiting("order-create")]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(string slug, CreateOrderInput input, CancellationToken ct)
    {
        var result = await orderCreation.CreateAsync(slug, input, User, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (result.Error is not null) return result.Error;
        return result.Created ? StatusCode(StatusCodes.Status201Created, result.Response) : Ok(result.Response);
    }
}
