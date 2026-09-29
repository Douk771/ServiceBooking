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
    ShopNotAccepting,

    /// <summary>ARCHITECTURE_CYCLE24.md §452.1 — the product is not sold on the chosen pickup date (its weekday mask, or the date's daily menu).</summary>
    NotOnThisDate
}

/// <summary>
/// ARCHITECTURE_CYCLE24.md §452.3 — the daily menu of ONE pickup date as <see cref="CatalogAvailability"/> reads it: either there is
/// none (the weekday mask decides) or there is one (ONLY its products are sold that day).
/// </summary>
public sealed class DailyMenuLookup
{
    private readonly IReadOnlySet<Guid>? _productIds;

    private DailyMenuLookup(IReadOnlySet<Guid>? productIds) => _productIds = productIds;

    public static DailyMenuLookup None { get; } = new(null);

    public static DailyMenuLookup ForMenu(IEnumerable<Guid> productIds) => new(productIds.ToHashSet());

    public bool Exists => _productIds is not null;

    public bool Contains(Guid productId) => _productIds?.Contains(productId) ?? false;
}

/// <summary>
/// ARCHITECTURE_CYCLE23.md §393.2 — the single "is this product available to a customer" rule. Its result is the
/// storefront's <c>available</c>, the owner's <c>availableToCustomers</c>, the problems of <c>quote</c> and the
/// refusal on order creation. New conditions of cycles 2–3 (weekdays, daily menu) are added HERE only — cycle 24 added the
/// pickup date: <c>Deleted → Unpublished → CategoryHidden → NotOnThisDate → SoldOut → InsufficientStock → ShopNotAccepting</c>.
/// </summary>
public static class CatalogAvailability
{
    /// <summary>The verdict for one product; <paramref name="freeStock"/> is the stock minus the active reserve (null = not computed, the product's own stock counts as free).</summary>
    /// <param name="product">The product.</param>
    /// <param name="category">Its category, null for the "Другое" block.</param>
    /// <param name="shopTracksStock">The shop's stock-tracking switch.</param>
    /// <param name="freeStock">Stock minus the active reserve; null when it was not computed.</param>
    /// <param name="shopAccepting">The result of ShopOrderingGate.</param>
    /// <param name="pickupDate">The pickup date the verdict is for; null = no date rules (cycle-23 behavior).</param>
    /// <param name="menu">The daily menu of <paramref name="pickupDate"/>; null = none.</param>
    public static ProductAvailability Evaluate(
        Product product, ProductCategory? category, bool shopTracksStock, int? freeStock, bool shopAccepting,
        DateOnly? pickupDate = null, DailyMenuLookup? menu = null)
    {
        if (product.DeletedAtUtc is not null) return ProductAvailability.Deleted;
        if (!product.IsPublished) return ProductAvailability.Unpublished;
        if (category is { IsHidden: true }) return ProductAvailability.CategoryHidden;
        if (pickupDate is { } date && !SoldOnDate(product, date, menu)) return ProductAvailability.NotOnThisDate;
        if (IsSoldOutOn(product, pickupDate)) return ProductAvailability.SoldOut;
        if (shopTracksStock && product.StockOnHand is not null)
        {
            var free = freeStock ?? product.StockOnHand.Value;
            var min = OrderQuantityRules.MinQuantity(product.Unit, product.WeightStepGrams, product.MinQuantityGrams);
            if (free < min) return ProductAvailability.InsufficientStock;
        }
        return shopAccepting ? ProductAvailability.Available : ProductAvailability.ShopNotAccepting;
    }

    public static bool IsAvailable(
        Product product, ProductCategory? category, bool shopTracksStock, int? freeStock, bool shopAccepting,
        DateOnly? pickupDate = null, DailyMenuLookup? menu = null) =>
        Evaluate(product, category, shopTracksStock, freeStock, shopAccepting, pickupDate, menu) == ProductAvailability.Available;

    /// <summary>A date with a daily menu sells exactly its products; a date without one follows the product's weekday mask.</summary>
    public static bool SoldOnDate(Product product, DateOnly date, DailyMenuLookup? menu) =>
        menu is { Exists: true } ? menu.Contains(product.Id) : WeekdayMask.Allows(product.AvailableWeekdaysMask, date);

    /// <summary>
    /// The "sold out" mark as it applies to a pickup date: "until cancelled" (no date) always; "for today" only on the day it was
    /// put on. With no date given the mark simply counts (cycle-23 behavior).
    /// </summary>
    public static bool IsSoldOutOn(Product product, DateOnly? pickupDate) =>
        product.IsSoldOut && (product.SoldOutForDate is null || pickupDate is null || product.SoldOutForDate == pickupDate);

    /// <summary>The mark as the owner's screens show it on <paramref name="today"/> (the shop's current working day): an expired "for today" mark reads as no mark.</summary>
    public static bool IsSoldOutNow(Product product, DateOnly today) =>
        product.IsSoldOut && (product.SoldOutForDate is null || product.SoldOutForDate >= today);
}
