using System.Linq.Expressions;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §501.2 — ONE expression for "the total of an order" in history, summary and the customer card, so the numbers cannot
/// diverge (R-5). An issued order counts by the actual weight, every other one by the estimate — the same rule as
/// <see cref="OrderDtoMapper.DisplayTotal"/> (a unit test keeps the two equal for all eight statuses).
/// </summary>
public static class OrderReportExpressions
{
    public static readonly Expression<Func<Order, decimal>> Total =
        o => o.Status == OrderStatus.Issued ? (o.FinalTotal ?? o.EstimatedTotal) : o.EstimatedTotal;

    private static readonly Func<Order, decimal> Compiled = Total.Compile();

    public static decimal TotalOf(Order order) => Compiled(order);
}
