using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>What the history is asked for — already validated and with the period resolved (see <c>ShopReportsController</c>).</summary>
public sealed record HistoryCriteria(
    Guid ShopId, ReportPeriod Period, IReadOnlyList<OrderStatus> Statuses, CustomerSearchTerm Customer, decimal? AmountFrom, decimal? AmountTo,
    int? Number, OrderHistorySort Sort, int Page, int PageSize);

public sealed record StatusAggregate(OrderStatus Status, int Count, decimal Amount);

public sealed record HistoryResult(List<HistoryRow> Rows, int TotalCount, int IssuedCount, decimal IssuedAmount);

/// <summary>A history row as the database returns it; texts are added by <c>ShopReportService</c>.</summary>
public sealed record HistoryRow(
    Guid OrderId, int Number, DateOnly PickupDate, DateTime PickupStartUtc, PickupKind PickupKind, OrderStatus Status, string? CustomerName,
    string? CustomerPhone, bool PersonalDataErased, bool HasWeightItems, decimal Total, int ItemCount);

/// <summary>
/// ARCHITECTURE_CYCLE25.md §501–§502 — the SQL of the order history and the summary. Everything is aggregated by the database (a shop can have
/// ~200 000 orders a year): one <c>GROUP BY Status</c> gives the totals of both screens, so they cannot disagree (R-5); the page is a second query. Every
/// filter is applied inside the <c>PickupDate</c> range (index <c>(CompanyId, PickupDate, …)</c>, the covering <c>IX_Orders_Report</c> for the totals).
/// </summary>
public sealed class OrderReportQueries(AppDbContext db)
{
    // ── history ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The orders of the shop within the period, before any other filter.</summary>
    public IQueryable<Order> InPeriod(Guid shopId, DateOnly from, DateOnly to) =>
        db.Orders.AsNoTracking().Where(o => o.CompanyId == shopId && o.PickupDate >= from && o.PickupDate <= to);

    private IQueryable<Order> Filtered(HistoryCriteria c)
    {
        var q = InPeriod(c.ShopId, c.Period.From, c.Period.To);
        var statuses = c.Statuses.ToArray();
        if (statuses.Length > 0) q = q.Where(o => statuses.Contains(o.Status));
        if (c.Number is { } number) q = q.Where(o => o.Number == number);
        if (c.AmountFrom is { } from) q = q.WhereTotal(t => t >= from);
        if (c.AmountTo is { } to) q = q.WhereTotal(t => t <= to);

        switch (c.Customer.Kind)
        {
            case CustomerSearchKind.Phone:
                // A fragment of the phone within the shop's OWN orders (staff search) — erased orders never match.
                var digits = c.Customer.Value;
                q = q.Where(o => !o.PersonalDataErased && o.CustomerPhone != null && EF.Functions.Like(o.CustomerPhone, "%" + digits + "%")); // SUBJECT-PHONE-GATE: not-account-scoped — shop's own orders within a shop-scoped staff search (US-25-05)
                break;
            case CustomerSearchKind.Name:
                var pattern = LikePattern.Contains(c.Customer.Value);
                q = q.Where(o => !o.PersonalDataErased && o.CustomerName != null && EF.Functions.ILike(o.CustomerName, pattern, "\\"));
                break;
        }
        return q;
    }

    public async Task<HistoryResult> HistoryAsync(HistoryCriteria c, CancellationToken ct)
    {
        var filtered = Filtered(c);
        var aggregates = await StatusAggregatesAsync(filtered, ct);
        var total = aggregates.Sum(a => a.Count);
        var issued = aggregates.FirstOrDefault(a => a.Status == OrderStatus.Issued);
        if (total == 0) return new HistoryResult([], 0, 0, 0m);

        var rows = await PageAsync(filtered, c.Sort, c.Page, c.PageSize, ct);
        return new HistoryResult(rows, total, issued?.Count ?? 0, issued?.Amount ?? 0m);
    }

    /// <summary>One page of rows (sorted by pickup time, ties by creation and id) with the item counts of just these orders.</summary>
    public async Task<List<HistoryRow>> PageAsync(IQueryable<Order> filtered, OrderHistorySort sort, int page, int pageSize, CancellationToken ct)
    {
        var ordered = sort == OrderHistorySort.PickupAsc
            ? filtered.OrderBy(o => o.PickupDate).ThenBy(o => o.PickupStartUtc).ThenBy(o => o.CreatedAtUtc).ThenBy(o => o.Id)
            : filtered.OrderByDescending(o => o.PickupDate).ThenByDescending(o => o.PickupStartUtc).ThenByDescending(o => o.CreatedAtUtc).ThenBy(o => o.Id);

        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id, o.Number, o.PickupDate, o.PickupStartUtc, o.PickupKind, o.Status, o.CustomerName, o.CustomerPhone, o.PersonalDataErased,
                o.HasWeightItems, o.EstimatedTotal, o.FinalTotal
            }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId))
            .GroupBy(i => i.OrderId).Select(g => new { OrderId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.OrderId, x => x.Count, ct);

        return rows.Select(r => new HistoryRow(
            r.Id, r.Number, r.PickupDate, r.PickupStartUtc, r.PickupKind, r.Status, r.CustomerName, r.CustomerPhone, r.PersonalDataErased,
            r.HasWeightItems,
            // The ONE total rule (OrderReportExpressions.Total), applied to the fetched fields.
            OrderReportExpressions.TotalOf(new Order { Status = r.Status, EstimatedTotal = r.EstimatedTotal, FinalTotal = r.FinalTotal }),
            counts.GetValueOrDefault(r.Id))).ToList();
    }

    /// <summary><c>SELECT Status, count(*), sum(Total) … GROUP BY Status</c> — one query for the totals of history, summary and the customer card.</summary>
    public async Task<List<StatusAggregate>> StatusAggregatesAsync(IQueryable<Order> orders, CancellationToken ct) =>
        (await orders.GroupBy(o => o.Status)
            .Select(OrderReportQueryableExtensions.UseTotal<OrderStatus, StatusAmount>(
                g => new StatusAmount(g.Key, g.Count(), g.Sum(o => o.EstimatedTotal))))
            .ToListAsync(ct))
        .Select(x => new StatusAggregate(x.Status, x.Count, x.Amount)).ToList();

    /// <summary>The shape of the grouped projection (a class, so a rewritten expression keeps its type).</summary>
    public sealed record StatusAmount(OrderStatus Status, int Count, decimal Amount);

    public sealed record DayAmount(DateOnly Date, int Orders, int IssuedCount, decimal Amount);

    // ── summary ─────────────────────────────────────────────────────────────────────────────────────────────

    public sealed record SummaryTopRow(Guid? ProductId, string Name, ProductUnit Unit, int Quantity, decimal Amount, bool IsDeleted);

    /// <summary>The top of the ISSUED orders of the period by product (a renamed product is one row): at most <paramref name="take"/> rows.</summary>
    public async Task<List<SummaryTopRow>> TopProductsAsync(Guid shopId, ReportPeriod period, SummaryTopSort sort, int take, CancellationToken ct)
    {
        var lines = from oi in db.OrderItems.AsNoTracking()
                    join o in InPeriod(shopId, period.From, period.To).Where(o => o.Status == OrderStatus.Issued) on oi.OrderId equals o.Id
                    select oi;
        var grouped = lines
            .GroupBy(i => new { i.ProductId, i.Unit, NameKey = i.ProductId == null ? i.NameSnapshot : null })
            .Select(g => new
            {
                g.Key.ProductId, g.Key.Unit, g.Key.NameKey,
                Quantity = g.Sum(i => i.QuantityActual ?? i.QuantityOrdered),
                Amount = g.Sum(i => i.LineTotalFinal ?? i.LineTotalEstimated)
            });

        // "By quantity": pieces in pieces, weight in kilograms (ARCHITECTURE_CYCLE25.md §519 p.9).
        var ordered = sort == SummaryTopSort.Quantity
            ? grouped.OrderByDescending(x => x.Unit == ProductUnit.Weight ? x.Quantity / 1000m : (decimal)x.Quantity).ThenByDescending(x => x.Amount)
            : grouped.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Quantity);
        var top = await ordered.ThenBy(x => x.ProductId).Take(take).ToListAsync(ct);

        var productIds = top.Where(t => t.ProductId != null).Select(t => t.ProductId!.Value).ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, Deleted = p.DeletedAtUtc != null }).ToDictionaryAsync(p => p.Id, ct);
        // A deleted product is named as in its latest order (the current name is gone from the catalogue's point of view).
        var deletedIds = products.Values.Where(p => p.Deleted).Select(p => p.Id).ToList();
        var latestNames = deletedIds.Count == 0
            ? []
            : (await db.OrderItems.AsNoTracking().Where(i => i.ProductId != null && deletedIds.Contains(i.ProductId.Value))
                .OrderByDescending(i => i.Order.CreatedAtUtc).Select(i => new { i.ProductId, i.NameSnapshot }).Take(deletedIds.Count * 20).ToListAsync(ct))
                .GroupBy(x => x.ProductId!.Value).ToDictionary(g => g.Key, g => g.First().NameSnapshot);

        return top.Select(t =>
        {
            if (t.ProductId is not { } id) return new SummaryTopRow(null, t.NameKey ?? string.Empty, t.Unit, t.Quantity, t.Amount, true);
            var product = products.GetValueOrDefault(id);
            var deleted = product is null || product.Deleted;
            var name = deleted ? latestNames.GetValueOrDefault(id) ?? product?.Name ?? string.Empty : product!.Name;
            return new SummaryTopRow(id, name, t.Unit, t.Quantity, t.Amount, deleted);
        }).ToList();
    }

    public sealed record DayAggregate(DateOnly Date, int Orders, int IssuedCount, decimal IssuedAmount);

    /// <summary>Orders, issued orders and issued amount per pickup day (P1). Days without orders are simply absent here; the caller fills them.</summary>
    public async Task<List<DayAggregate>> DaysAsync(Guid shopId, ReportPeriod period, CancellationToken ct) =>
        (await InPeriod(shopId, period.From, period.To).GroupBy(o => o.PickupDate).OrderBy(g => g.Key)
            .Select(OrderReportQueryableExtensions.UseTotal<DateOnly, DayAmount>(g => new DayAmount(
                g.Key, g.Count(), g.Count(o => o.Status == OrderStatus.Issued),
                g.Where(o => o.Status == OrderStatus.Issued).Sum(o => o.EstimatedTotal))))
            .ToListAsync(ct))
        .Select(x => new DayAggregate(x.Date, x.Orders, x.IssuedCount, x.Amount)).ToList();
}

/// <summary>Composes the shared total expression with a predicate on it, so a filter by amount uses the SAME rule as the totals.</summary>
internal static class OrderReportQueryableExtensions
{
    public static IQueryable<Order> WhereTotal(this IQueryable<Order> orders, Expression<Func<decimal, bool>> predicate)
    {
        var selector = OrderReportExpressions.Total;
        var body = new ParameterReplacer(predicate.Parameters[0], selector.Body).Visit(predicate.Body);
        return orders.Where(Expression.Lambda<Func<Order, bool>>(body, selector.Parameters));
    }

    /// <summary>
    /// <c>g.Sum(selector)</c> inside a grouped projection takes a delegate, so the shared <see cref="OrderReportExpressions.Total"/> cannot be passed to it
    /// directly. The template writes <c>Sum(o => o.EstimatedTotal)</c> as a placeholder and this rewrites EVERY such sum to the shared expression — the rule
    /// still lives in one place (R-5).
    /// </summary>
    public static Expression<Func<IGrouping<TKey, Order>, TResult>> UseTotal<TKey, TResult>(Expression<Func<IGrouping<TKey, Order>, TResult>> template) =>
        (Expression<Func<IGrouping<TKey, Order>, TResult>>)new SumRewriter().Visit(template);

    private sealed class SumRewriter : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Enumerable) && node.Method.Name == nameof(Enumerable.Sum) && node.Arguments.Count == 2 &&
                node.Method.GetParameters()[1].ParameterType == typeof(Func<Order, decimal>))
                return Expression.Call(node.Method, Visit(node.Arguments[0]), OrderReportExpressions.Total);
            return base.VisitMethodCall(node);
        }
    }

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : base.VisitParameter(node);
    }
}
