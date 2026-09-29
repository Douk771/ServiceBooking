using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.4 — the ONE money rule of an order, normative in kopecks
/// (contracts/cycle23/order-money-vectors.json is the shared test vector set for this class and for the
/// frontend twin goods/src/utils/orderMoney.ts):
/// piece line = priceKop × quantity; weight line (quantity in grams, price per 1 kg) =
/// floor((priceKop × grams + 500) / 1000), i.e. round-half-up to a kopeck; total = sum of lines.
/// Pure integer arithmetic on purpose — no double, no banker's rounding.
/// </summary>
public static class OrderMoney
{
    /// <summary>Price in kopecks (prices are stored with 2 decimals; anything finer is rounded half-up).</summary>
    public static long ToKopecks(decimal price) =>
        decimal.ToInt64(Math.Round(price * 100m, MidpointRounding.AwayFromZero));

    public static decimal FromKopecks(long kopecks) => kopecks / 100m;

    public static long LineKopecks(ProductUnit unit, decimal unitPrice, int quantity)
    {
        var priceKop = ToKopecks(unitPrice);
        return unit == ProductUnit.Weight
            ? (priceKop * quantity + 500) / 1000
            : priceKop * quantity;
    }

    public static decimal LineTotal(ProductUnit unit, decimal unitPrice, int quantity) =>
        FromKopecks(LineKopecks(unit, unitPrice, quantity));

    public static decimal Sum(IEnumerable<decimal> lineTotals) => lineTotals.Sum(t => FromKopecks(ToKopecks(t)));

    /// <summary>A weight line is only an estimate until the order is issued by the actual weight.</summary>
    public static bool IsApproximate(ProductUnit unit) => unit == ProductUnit.Weight;
}
