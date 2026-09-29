using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>What is needed once per request to turn products into DTOs.</summary>
public sealed record CatalogContext(ShopSettings Settings, bool Accepting, IReadOnlyDictionary<Guid, ProductCategory> Categories);

/// <summary>
/// ARCHITECTURE_CYCLE23.md §393.2–§393.3 — product → owner/staff <see cref="ProductDto"/>. The customer-facing verdict
/// (<c>availableToCustomers</c>) comes from the single rule <see cref="CatalogAvailability"/>, so the cabinet shows exactly
/// what a customer will see.
/// </summary>
public sealed class CatalogMapper(AppDbContext db, StockLedger stockLedger)
{
    public async Task<CatalogContext> LoadContextAsync(Company shop, CancellationToken ct = default)
    {
        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings { CompanyId = shop.Id };
        var categories = await db.ProductCategories.AsNoTracking().Where(c => c.CompanyId == shop.Id).ToDictionaryAsync(c => c.Id, ct);
        return new CatalogContext(settings, ShopOrderingGate.Evaluate(shop, settings, DateTime.UtcNow).Accepting, categories);
    }

    public static ProductDto ToDto(Product p, CatalogContext ctx, int reserved)
    {
        var category = p.CategoryId is { } cid ? ctx.Categories.GetValueOrDefault(cid) : null;
        var free = StockLedger.Free(p.StockOnHand, reserved);
        var available = CatalogAvailability.IsAvailable(p, category, ctx.Settings.TrackStock, free, ctx.Accepting);
        return new ProductDto(
            p.Id, p.CategoryId, p.Name, p.Description, p.ImageUrl, p.ThumbnailUrl, p.Unit, p.Price, p.PortionText, p.WeightStepGrams,
            OrderQuantityRules.MinQuantity(p.Unit, p.WeightStepGrams, p.MinQuantityGrams), OrderQuantityRules.MaxQuantity(p.Unit),
            p.Position, p.IsPublished, p.IsSoldOut, new FoodInfoDto(p.CompositionAndAllergens), StockLedger.ToDto(p, reserved), available);
    }

    /// <summary>One product with its current reserve (an extra GROUP BY limited to that product).</summary>
    public async Task<ProductDto> BuildOneAsync(Company shop, Product product, CancellationToken ct = default)
    {
        var ctx = await LoadContextAsync(shop, ct);
        var reserved = await stockLedger.GetReservedAsync(shop.Id, [product.Id], ct);
        return ToDto(product, ctx, reserved.GetValueOrDefault(product.Id));
    }

    /// <summary>Products in catalog order: by the category's position (no category last), then by their own position.</summary>
    public async Task<List<ProductDto>> BuildManyAsync(Company shop, IReadOnlyList<Product> products, CancellationToken ct = default)
    {
        var ctx = await LoadContextAsync(shop, ct);
        var reserved = await stockLedger.GetReservedAsync(shop.Id, ct: ct);
        return products
            .OrderBy(p => p.CategoryId is { } c && ctx.Categories.TryGetValue(c, out var cat) ? cat.Position : int.MaxValue)
            .ThenBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .Select(p => ToDto(p, ctx, reserved.GetValueOrDefault(p.Id))).ToList();
    }
}
