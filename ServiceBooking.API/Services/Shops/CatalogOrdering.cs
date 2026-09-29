using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Shops;

/// <summary>Thrown when an order list is not exactly the current set of ids (a stale screen) — answered as 400 with its message.</summary>
public sealed class InvalidCatalogReorderException(string message) : Exception(message);

/// <summary>
/// ARCHITECTURE_CYCLE23.md §393.1 — pure ordering logic of categories and products (the CompanyPhotoOrdering pattern): the caller
/// loads the current rows, one of these methods rewrites <c>Position</c> in place, and the caller saves inside a transaction
/// held under the advisory lock <c>shop-catalog:{companyId}</c>. Positions are compacted to 0..n-1 by the server.
/// </summary>
public static class CatalogOrdering
{
    /// <summary>A full permutation of the current categories; anything else (partial list, unknown or repeated id) throws.</summary>
    public static void ApplyCategoryOrder(IReadOnlyList<ProductCategory> current, IReadOnlyList<Guid> ids)
    {
        Apply(current.ToDictionary(c => c.Id), ids, ShopTexts.CategoriesOutdated, (c, position) => c.Position = position);
    }

    /// <summary>A full permutation of the products of ONE category (the caller passes exactly that category's live products).</summary>
    public static void ApplyProductOrder(IReadOnlyList<Product> current, IReadOnlyList<Guid> ids)
    {
        Apply(current.ToDictionary(p => p.Id), ids, ShopTexts.ProductsOutdated, (p, position) => p.Position = position);
    }

    /// <summary>Renumbers the remaining categories to 0..n-1 keeping their order (after a delete).</summary>
    public static void Compact(IReadOnlyList<ProductCategory> remaining)
    {
        var ordered = remaining.OrderBy(c => c.Position).ThenBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Position = i;
    }

    /// <summary>The position a new row takes: the end of the list.</summary>
    public static int NextPosition(IEnumerable<int> existingPositions) => existingPositions.Any() ? existingPositions.Max() + 1 : 0;

    private static void Apply<T>(Dictionary<Guid, T> byId, IReadOnlyList<Guid> ids, string error, Action<T, int> setPosition)
    {
        if (ids.Count != byId.Count || ids.Distinct().Count() != ids.Count || ids.Any(id => !byId.ContainsKey(id)))
            throw new InvalidCatalogReorderException(error);
        for (var i = 0; i < ids.Count; i++) setPosition(byId[ids[i]], i);
    }
}
