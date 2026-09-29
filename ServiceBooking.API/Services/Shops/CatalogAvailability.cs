using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Shops;

public enum ProductAvailability
{
    Available,
    Deleted,
    Unpublished,
    CategoryHidden,
    SoldOut,
    InsufficientStock,
    ShopNotAccepting
}

/// <summary>
/// ARCHITECTURE_CYCLE23.md §393.2 — the single "is this product available to a customer" rule. Its result is the
/// storefront's <c>available</c>, the owner's <c>availableToCustomers</c>, the problems of <c>quote</c> and the
/// refusal on order creation. New conditions of cycles 2–3 (weekdays, daily menu) are added HERE only.
/// </summary>
public static class CatalogAvailability
{
    /// <summary>The verdict for one product; <paramref name="freeStock"/> is the stock minus the active reserve (null = not computed, the product's own stock counts as free).</summary>
    /// <param name="product">The product.</param>
    /// <param name="category">Its category, null for the "Другое" block.</param>
    /// <param name="shopTracksStock">The shop's stock-tracking switch.</param>
    /// <param name="freeStock">Stock minus the active reserve; null when it was not computed.</param>
    /// <param name="shopAccepting">The result of ShopOrderingGate.</param>
    public static ProductAvailability Evaluate(
        Product product, ProductCategory? category, bool shopTracksStock, int? freeStock, bool shopAccepting)
    {
        if (product.DeletedAtUtc is not null) return ProductAvailability.Deleted;
        if (!product.IsPublished) return ProductAvailability.Unpublished;
        if (category is { IsHidden: true }) return ProductAvailability.CategoryHidden;
        if (product.IsSoldOut) return ProductAvailability.SoldOut;
        if (shopTracksStock && product.StockOnHand is not null)
        {
            var free = freeStock ?? product.StockOnHand.Value;
            var min = OrderQuantityRules.MinQuantity(product.Unit, product.WeightStepGrams, product.MinQuantityGrams);
            if (free < min) return ProductAvailability.InsufficientStock;
        }
        return shopAccepting ? ProductAvailability.Available : ProductAvailability.ShopNotAccepting;
    }

    public static bool IsAvailable(
        Product product, ProductCategory? category, bool shopTracksStock, int? freeStock, bool shopAccepting) =>
        Evaluate(product, category, shopTracksStock, freeStock, shopAccepting) == ProductAvailability.Available;
}
