using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>Either the error the action returns as is, or the up-to-date order for the response.</summary>
public sealed record OrderActionResult(ActionResult? Error, StaffOrderDto? Order = null);

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396 — the staff transitions of an order (accept, reject, ready, not picked up, cancel by the shop) and the
/// issue with the actual weights. Every action carries <c>expectedVersion</c>: the order is loaded, compared, changed, its Version
/// incremented and saved under the EF concurrency token; a mismatch or a lost race is a 409 <c>VersionMismatch</c> carrying the CURRENT
/// order — the action is NOT applied and the frontend replaces its card (it never retries on its own). The status machine is
/// <see cref="OrderStateMachine"/>; the journal row and the board revision are written by <see cref="OrderEventLog"/> in the same transaction.
/// </summary>
public class OrderTransitionService(
    AppDbContext db, OrderEventLog eventLog, OrderActorResolver actorResolver, StockLedger stockLedger, StaffOrderDtoFactory staffDtos)
{
    public const string ReasonTooLong = "Причина — не длиннее 300 символов";
    public const string ActualWeightRequired = "Укажите фактический вес каждой весовой позиции";
    public const string ActualWeightRange = "Фактический вес — от 1 до 100 000 г";

    // ── Accept / Reject / Ready / NotPickedUp / Cancel ───────────────────────────────────────────────────────────

    public async Task<OrderActionResult> ApplyAsync(
        Company shop, Guid orderId, OrderAction action, int expectedVersion, string? reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is { Length: > 300 }) return new OrderActionResult(new BadRequestObjectResult(ReasonTooLong));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LoadTrackedAsync(shop.Id, orderId, ct);
        if (order is null) return new OrderActionResult(new NotFoundResult());

        if (order.Version != expectedVersion) return await VersionMismatchAsync(shop, order, ct);
        if (!OrderStateMachine.TryApply(order.Status, action, out var to))
            return await ConflictAsync(shop, order, OrderConflictCode.InvalidTransition, OrderTexts.InvalidTransition(order.Status), ct);

        var from = order.Status;
        var now = DateTime.UtcNow;
        order.Status = to;
        order.UpdatedAtUtc = now;
        if (action == OrderAction.Accept) order.AcceptedAtUtc = now;
        if (action == OrderAction.MarkReady) order.ReadyAtUtc = now;
        if (OrderStateMachine.IsTerminal(to)) order.CompletedAtUtc = now;
        // The reason is only for a refusal or a cancellation by the shop; the customer sees it.
        if (action is OrderAction.Reject or OrderAction.Cancel) order.StatusReason = trimmedReason;
        order.Version++;

        var kind = action switch
        {
            OrderAction.Accept => OrderEventKind.Accepted,
            OrderAction.Reject => OrderEventKind.Rejected,
            OrderAction.MarkReady => OrderEventKind.MarkedReady,
            OrderAction.NotPickedUp => OrderEventKind.NotPickedUp,
            _ => OrderEventKind.CancelledByShop
        };
        await eventLog.AppendAsync(order, kind, await actorResolver.ResolveStaffAsync(user), from, to,
            reason: action is OrderAction.Reject or OrderAction.Cancel ? trimmedReason : null);

        return await SaveAndRespondAsync(shop, order, transaction, ct);
    }

    // ── Issue ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The amount to pay for the given actual weights — the same arithmetic the issue will store. Changes nothing.</summary>
    public async Task<(ActionResult? Error, IssueQuoteDto? Quote)> QuoteIssueAsync(
        Company shop, Guid orderId, List<ActualQuantityInput>? actuals, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == shop.Id, ct);
        if (order is null) return (new NotFoundResult(), null);
        if (order.Status != OrderStatus.Ready)
            return (Conflict(OrderConflictCode.InvalidTransition, OrderTexts.InvalidTransition(order.Status),
                await staffDtos.BuildAsync(await LoadWithEventsAsync(order.Id, ct), shop, ct)), null);

        var plan = PlanIssue(order, actuals);
        if (plan.Error is not null) return (new BadRequestObjectResult(plan.Error), null);

        var lines = order.Items.OrderBy(i => i.Position).Select(i =>
        {
            var line = plan.Lines[i.Id];
            return new StaffOrderItemDto(i.Id, i.ProductId, i.NameSnapshot, i.Unit, i.UnitPrice, i.PortionTextSnapshot,
                i.WeightStepGrams, i.QuantityOrdered, line.Actual, line.Total, IsApproximate: false);
        }).ToList();
        return (null, new IssueQuoteDto(lines, plan.FinalTotal));
    }

    /// <summary>
    /// Issues a Ready order: stores the actual quantities and the final amounts, and writes the stock off — under the shop's stock lock,
    /// only for lines that reserved stock and only if the product's stock is tracked right now. A shortage is NOT an error: the stock
    /// goes down to zero and the journal records "written off N of M" (not shown to the customer).
    /// </summary>
    public async Task<OrderActionResult> IssueAsync(
        Company shop, Guid orderId, int expectedVersion, List<ActualQuantityInput>? actuals, ClaimsPrincipal user, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await stockLedger.LockAsync(shop.Id); // lock order everywhere: shop-stock → the settings row (via the event log)
        var order = await LoadTrackedAsync(shop.Id, orderId, ct);
        if (order is null) return new OrderActionResult(new NotFoundResult());

        if (order.Version != expectedVersion) return await VersionMismatchAsync(shop, order, ct);
        if (!OrderStateMachine.TryApply(order.Status, OrderAction.Issue, out var to))
            return await ConflictAsync(shop, order, OrderConflictCode.InvalidTransition, OrderTexts.InvalidTransition(order.Status), ct);

        var plan = PlanIssue(order, actuals);
        if (plan.Error is not null) return new OrderActionResult(new BadRequestObjectResult(plan.Error));

        var writeOffs = new List<StockWriteOff>();
        foreach (var item in order.Items.OrderBy(i => i.Position))
        {
            var line = plan.Lines[item.Id];
            item.QuantityActual = line.Actual;
            item.LineTotalFinal = line.Total;

            if (!item.ReservesStock || item.ProductId is null) continue;
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, ct);
            if (product?.StockOnHand is not { } onHand) continue; // tracking was switched off for this product since — nothing to write off

            var written = Math.Min(line.Actual, onHand);
            product.StockOnHand = onHand - written;
            product.UpdatedAtUtc = DateTime.UtcNow;
            writeOffs.Add(new StockWriteOff(item.NameSnapshot, item.Unit, line.Actual, written, Zeroed: written < line.Actual));
        }

        var before = order.EstimatedTotal;
        var from = order.Status;
        var now = DateTime.UtcNow;
        order.FinalTotal = plan.FinalTotal;
        order.Status = to;
        order.CompletedAtUtc = now;
        order.UpdatedAtUtc = now;
        order.Version++;
        await eventLog.AppendAsync(order, OrderEventKind.Issued, await actorResolver.ResolveStaffAsync(user), from, to,
            changesJson: writeOffs.Count > 0 ? OrderChangeLog.SerializeIssue(new IssueLog(writeOffs)) : null,
            totalBefore: before, totalAfter: plan.FinalTotal);

        return await SaveAndRespondAsync(shop, order, transaction, ct);
    }

    private sealed record IssueLine(int Actual, decimal Total);
    private sealed record IssuePlan(string? Error, Dictionary<Guid, IssueLine> Lines, decimal FinalTotal);

    /// <summary>Validates the actual weights (exactly one per weight line, none for pieces, 1..100 000 g) and computes every final line and the total.</summary>
    private static IssuePlan PlanIssue(Order order, List<ActualQuantityInput>? actuals)
    {
        var provided = actuals ?? [];
        var weightIds = order.Items.Where(i => i.Unit == ProductUnit.Weight).Select(i => i.Id).ToHashSet();
        if (provided.Count != weightIds.Count || provided.Select(a => a.ItemId).Distinct().Count() != provided.Count ||
            provided.Any(a => !weightIds.Contains(a.ItemId)))
            return new IssuePlan(ActualWeightRequired, [], 0m);
        if (provided.Any(a => !OrderQuantityRules.IsValidActualWeight(a.Quantity)))
            return new IssuePlan(ActualWeightRange, [], 0m);

        var byItem = provided.ToDictionary(a => a.ItemId, a => a.Quantity);
        var lines = order.Items.ToDictionary(
            i => i.Id,
            i =>
            {
                var actual = i.Unit == ProductUnit.Weight ? byItem[i.Id] : i.QuantityOrdered;
                return new IssueLine(actual, OrderMoney.LineTotal(i.Unit, i.UnitPrice, actual));
            });
        return new IssuePlan(null, lines, OrderMoney.Sum(lines.Values.Select(l => l.Total)));
    }

    // ── shared plumbing ──────────────────────────────────────────────────────────────────────────────────────────

    private Task<Order?> LoadTrackedAsync(Guid shopId, Guid orderId, CancellationToken ct) =>
        db.Orders.Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == shopId, ct);

    private async Task<Order> LoadWithEventsAsync(Guid orderId, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery().FirstAsync(o => o.Id == orderId, ct);

    /// <summary>Saves under the concurrency token; a lost race becomes a VersionMismatch with the current order, never an exception.</summary>
    private async Task<OrderActionResult> SaveAndRespondAsync(
        Company shop, Order order, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        var orderId = order.Id;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var fresh = await LoadWithEventsAsync(orderId, ct);
            return new OrderActionResult(Conflict(OrderConflictCode.VersionMismatch, OrderTexts.VersionMismatch, await staffDtos.BuildAsync(fresh, shop, ct)));
        }
        db.ChangeTracker.Clear();
        return new OrderActionResult(null, await staffDtos.BuildAsync(await LoadWithEventsAsync(orderId, ct), shop, ct));
    }

    private async Task<OrderActionResult> VersionMismatchAsync(Company shop, Order order, CancellationToken ct) =>
        await ConflictAsync(shop, order, OrderConflictCode.VersionMismatch, OrderTexts.VersionMismatch, ct);

    private async Task<OrderActionResult> ConflictAsync(Company shop, Order order, OrderConflictCode code, string message, CancellationToken ct)
    {
        // The order in the body must be the CURRENT one, with its journal — reloaded clean, not the half-changed tracked graph.
        var orderId = order.Id;
        db.ChangeTracker.Clear();
        return new OrderActionResult(Conflict(code, message, await staffDtos.BuildAsync(await LoadWithEventsAsync(orderId, ct), shop, ct)));
    }

    private static ConflictObjectResult Conflict(OrderConflictCode code, string message, StaffOrderDto? order) =>
        new(new OrderConflictDto(code, message, Order: order));
}
