using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §394 — the stock side of orders. The reserve is NEVER stored: it is computed from the active
/// orders (New/Accepted/Ready) here, so a drifting counter cannot exist by construction (§394.2). Everything that DECREASES
/// the free stock (creating an order, growing an edit, issuing, setting the stock figure) runs in a transaction under
/// <see cref="LockAsync"/> — one advisory lock per shop — and recomputes the reserve inside the lock. Increasing the free
/// stock (cancel, reject, not picked up) needs no lock: the order simply stops being active.
/// </summary>
public class StockLedger(AppDbContext db)
{
    /// <summary>Must be called inside an open transaction; released when it ends. Lock order: this one first, then the settings row (via OrderEventLog).</summary>
    public Task LockAsync(Guid companyId) => AdvisoryLock.AcquireAsync(db, $"shop-stock:{companyId}");

    /// <summary>
    /// Σ QuantityOrdered of the reserving lines of the shop's active orders, per product. Products with no reserve are
    /// absent. <paramref name="productIds"/> null = every product of the shop (the storefront asks this way, in ONE GROUP BY).
    /// </summary>
    public async Task<Dictionary<Guid, int>> GetReservedAsync(
        Guid companyId, IReadOnlyCollection<Guid>? productIds = null, CancellationToken ct = default)
    {
        var query = db.OrderItems.AsNoTracking()
            .Where(i => i.ReservesStock && i.ProductId != null && i.Order.CompanyId == companyId &&
                        (i.Order.Status == Core.Enums.OrderStatus.New || i.Order.Status == Core.Enums.OrderStatus.Accepted ||
                         i.Order.Status == Core.Enums.OrderStatus.Ready));
        if (productIds is not null) query = query.Where(i => productIds.Contains(i.ProductId!.Value));

        var rows = await query.GroupBy(i => i.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Reserved = g.Sum(i => i.QuantityOrdered) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ProductId, r => r.Reserved);
    }

    /// <summary>Free stock = on hand − reserved; null when the product's stock is not tracked. May be negative (stock was corrected downward).</summary>
    public static int? Free(int? onHand, int reserved) => onHand is null ? null : onHand.Value - reserved;

    public static StockDto ToDto(Product product, int reserved) =>
        new(product.StockOnHand, reserved, Free(product.StockOnHand, reserved));
}
