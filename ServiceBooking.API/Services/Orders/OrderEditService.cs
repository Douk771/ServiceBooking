using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.3 (US-23-24) — staff editing the content of a live order (New/Accepted/Ready). The request is the
/// FULL desired content: lines with <c>itemId</c> (a new quantity of an existing line) and lines with <c>productId</c> (a new line at the
/// CURRENT catalog price — replacing a line is "remove the old + add the new"); anything not listed is removed. The order is never empty
/// (that is "reject/cancel", 409 <c>LastItemCannotBeRemoved</c>). Stock is checked for growth only — against the free stock plus the
/// line's own reserve — under the shop's stock lock when tracking is on. The journal row records was → became, the totals and the
/// comment for the customer; the order is marked modified and its version moves on.
/// </summary>
public class OrderEditService(
    AppDbContext db, OrderEventLog eventLog, OrderActorResolver actorResolver, StockLedger stockLedger, StaffOrderDtoFactory staffDtos,
    ShopGateLoader gates, OrderNumberAllocator numberAllocator)
{
    public const string CompositionInvalid = "Состав заказа указан с ошибкой";
    public const string CommentTooLong = "Комментарий — не длиннее 500 символов";

    public async Task<OrderActionResult> EditAsync(Company shop, Guid orderId, EditOrderInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        var comment = string.IsNullOrWhiteSpace(input.CommentForCustomer) ? null : input.CommentForCustomer.Trim();
        if (comment is { Length: > 500 }) return new OrderActionResult(new BadRequestObjectResult(CommentTooLong));

        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings { CompanyId = shop.Id };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (settings.TrackStock) await stockLedger.LockAsync(shop.Id);

        var order = await db.Orders.Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == shop.Id, ct);
        if (order is null) return new OrderActionResult(new NotFoundResult());

        if (order.Version != input.ExpectedVersion)
            return await ConflictAsync(shop, order.Id, OrderConflictCode.VersionMismatch, OrderTexts.VersionMismatch, null, ct);
        if (!OrderStateMachine.IsAllowed(order.Status, OrderAction.Edit))
            return await ConflictAsync(shop, order.Id, OrderConflictCode.InvalidTransition, OrderTexts.InvalidTransition(order.Status), null, ct);

        var lines = input.Items ?? [];
        if (lines.Count == 0)
            return await ConflictAsync(shop, order.Id, OrderConflictCode.LastItemCannotBeRemoved, OrderTexts.LastItemCannotBeRemoved, null, ct);

        // A line is either an existing one (itemId) or a new one (productId) — never both, never neither; itemIds are this order's, once each.
        var itemById = order.Items.ToDictionary(i => i.Id);
        var seenItems = new HashSet<Guid>();
        foreach (var line in lines)
        {
            if ((line.ItemId is null) == (line.ProductId is null)) return Bad(CompositionInvalid);
            if (line.ItemId is { } itemId && (!itemById.ContainsKey(itemId) || !seenItems.Add(itemId))) return Bad(CompositionInvalid);
        }

        // Products: for new lines (must be live products of this shop) and for existing lines (their stock figure).
        var productIds = lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value)
            .Concat(order.Items.Where(i => i.ProductId is not null).Select(i => i.ProductId!.Value)).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => p.CompanyId == shop.Id && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var line in lines.Where(l => l.ProductId is not null))
            if (!products.TryGetValue(line.ProductId!.Value, out var np) || np.DeletedAtUtc is not null)
                return await ConflictAsync(shop, order.Id, OrderConflictCode.ProductUnavailable, OrderTexts.ProductUnavailable, null, ct);

        // Quantity rules: by the snapshot's step for existing lines, the product's step for new ones.
        var problems = new List<OrderProblemDto>();
        foreach (var line in lines)
        {
            if (line.ItemId is { } id)
            {
                var item = itemById[id];
                if (!OrderQuantityRules.IsValidForEdit(item.Unit, line.Quantity, item.WeightStepGrams))
                    problems.Add(new OrderProblemDto(item.ProductId ?? Guid.Empty, item.NameSnapshot, OrderProblemReason.InvalidQuantity, OrderTexts.InvalidQuantity));
            }
            else
            {
                var product = products[line.ProductId!.Value];
                if (!OrderQuantityRules.IsValidForEdit(product.Unit, line.Quantity, product.WeightStepGrams))
                    problems.Add(new OrderProblemDto(product.Id, product.Name, OrderProblemReason.InvalidQuantity, OrderTexts.InvalidQuantity));
            }
        }
        if (problems.Count > 0)
            return await ConflictAsync(shop, order.Id, OrderConflictCode.InvalidQuantity, OrderTexts.InvalidQuantity, problems, ct);

        // Stock: only GROWTH is checked, against free stock + the line's own reserve; several lines of one product share what is free.
        var reserved = settings.TrackStock ? await stockLedger.GetReservedAsync(shop.Id, productIds, ct) : new Dictionary<Guid, int>();
        var remainingFree = new Dictionary<Guid, int>();
        int Free(Guid productId, Product p) =>
            remainingFree.TryGetValue(productId, out var v) ? v : remainingFree[productId] = p.StockOnHand!.Value - reserved.GetValueOrDefault(productId);

        var newLineReserves = new Dictionary<EditOrderLineInput, bool>();
        foreach (var line in lines)
        {
            if (!settings.TrackStock) break;
            if (line.ItemId is { } id)
            {
                var item = itemById[id];
                if (!item.ReservesStock || item.ProductId is not { } pid || !products.TryGetValue(pid, out var p) || p.StockOnHand is null) continue;
                var growth = line.Quantity - item.QuantityOrdered;
                if (growth <= 0) { remainingFree[pid] = Free(pid, p) - growth; continue; } // a decrease returns stock to the pool
                var free = Free(pid, p);
                if (growth > free)
                    problems.Add(StockProblem(p, item.QuantityOrdered + Math.Max(0, free), item.WeightStepGrams));
                else
                    remainingFree[pid] = free - growth;
            }
            else
            {
                var p = products[line.ProductId!.Value];
                var reserves = p.StockOnHand is not null;
                newLineReserves[line] = reserves;
                if (!reserves) continue;
                var free = Free(p.Id, p);
                if (line.Quantity > free)
                    problems.Add(StockProblem(p, Math.Max(0, free), p.WeightStepGrams));
                else
                    remainingFree[p.Id] = free - line.Quantity;
            }
        }
        if (problems.Count > 0)
            return await ConflictAsync(shop, order.Id, OrderConflictCode.InsufficientStock, OrderTexts.InsufficientStock, problems, ct);

        // Apply. The change list is built from what really differs.
        var totalBefore = order.EstimatedTotal;
        var changes = new List<ChangeEntry>();
        foreach (var item in order.Items.Where(i => !seenItems.Contains(i.Id)).ToList())
        {
            changes.Add(new ChangeEntry(item.NameSnapshot, new ChangeSide(item.QuantityOrdered, item.UnitPrice, item.Unit), null));
            db.OrderItems.Remove(item);
            order.Items.Remove(item);
        }

        var position = 0;
        foreach (var line in lines)
        {
            if (line.ItemId is { } id)
            {
                var item = itemById[id];
                if (item.QuantityOrdered != line.Quantity)
                    changes.Add(new ChangeEntry(item.NameSnapshot,
                        new ChangeSide(item.QuantityOrdered, item.UnitPrice, item.Unit), new ChangeSide(line.Quantity, item.UnitPrice, item.Unit)));
                item.QuantityOrdered = line.Quantity;
                item.LineTotalEstimated = OrderMoney.LineTotal(item.Unit, item.UnitPrice, line.Quantity);
                item.Position = position++;
            }
            else
            {
                var p = products[line.ProductId!.Value];
                var added = new OrderItem
                {
                    Id = Guid.NewGuid(), OrderId = order.Id, Position = position++, ProductId = p.Id, NameSnapshot = p.Name, Unit = p.Unit,
                    UnitPrice = p.Price, PortionTextSnapshot = p.PortionText, WeightStepGrams = p.WeightStepGrams,
                    QuantityOrdered = line.Quantity, LineTotalEstimated = OrderMoney.LineTotal(p.Unit, p.Price, line.Quantity),
                    ReservesStock = settings.TrackStock && p.StockOnHand is not null,
                };
                // Add() marks the entity Added (client-side Guid key) and EF fixup puts it into order.Items once — do not add it there manually.
                db.OrderItems.Add(added);
                changes.Add(new ChangeEntry(p.Name, null, new ChangeSide(line.Quantity, p.Price, p.Unit)));
            }
        }

        // Nothing actually changed: no version bump, no journal row, no "modified" mark.
        if (changes.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new OrderActionResult(null, await staffDtos.BuildAsync(await LoadWithEventsAsync(order.Id, ct), shop, ct));
        }

        order.EstimatedTotal = OrderMoney.Sum(order.Items.Select(i => i.LineTotalEstimated));
        order.HasWeightItems = order.Items.Any(i => i.Unit == ProductUnit.Weight);
        order.IsModifiedByShop = true;
        order.UpdatedAtUtc = DateTime.UtcNow;
        order.Version++;
        await eventLog.AppendAsync(order, OrderEventKind.Edited, await actorResolver.ResolveStaffAsync(user), order.Status, order.Status,
            comment: comment, changesJson: OrderChangeLog.SerializeEdit(changes), totalBefore: totalBefore, totalAfter: order.EstimatedTotal);

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new OrderActionResult(Conflict(OrderConflictCode.VersionMismatch, OrderTexts.VersionMismatch, null,
                await staffDtos.BuildAsync(await LoadWithEventsAsync(orderId, ct), shop, ct)));
        }
        db.ChangeTracker.Clear();
        return new OrderActionResult(null, await staffDtos.BuildAsync(await LoadWithEventsAsync(orderId, ct), shop, ct));
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE24.md §451.4 (US-24-09, P1) — staff move the pickup time at the customer's request. Only New / Accepted, with
    /// <c>expectedVersion</c>. Staff may pick any slot of a working day inside the horizon that has not ended yet, or "as soon as possible" while the
    /// shop is open — a pause and the preparation time do not stop them (they are a decision of the shop's people, not a rule for customers). If the
    /// PICKUP DATE changes, the order gets the next NUMBER of the new day in the same transaction (numbers are unique within a pickup day); the journal
    /// keeps both, and the customer is told the new number by the planner. A request that changes nothing writes nothing.
    /// </summary>
    public async Task<OrderActionResult> ChangePickupAsync(
        Company shop, Guid orderId, ChangePickupInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (comment is { Length: > 500 }) return new OrderActionResult(new BadRequestObjectResult(CommentTooLong));
        if (input.Pickup is null || input.Pickup is { Kind: PickupKind.Slot } && (input.Pickup.Date is null || input.Pickup.SlotStartUtc is null))
            return new OrderActionResult(new BadRequestObjectResult(OrderCreationService.PickupSelectionIncomplete));

        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(shop, now, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == shop.Id, ct);
        if (order is null) return new OrderActionResult(new NotFoundResult());

        if (order.Version != input.ExpectedVersion)
            return await ConflictAsync(shop, order.Id, OrderConflictCode.VersionMismatch, OrderTexts.VersionMismatch, null, ct);
        if (!OrderStateMachine.IsAllowed(order.Status, OrderAction.ChangePickup))
            return await ConflictAsync(shop, order.Id, OrderConflictCode.InvalidTransition, OrderTexts.InvalidTransition(order.Status), null, ct);

        var verdict = context.Pickup.Validate(new PickupSelection(input.Pickup.Kind, input.Pickup.Date, input.Pickup.SlotStartUtc), now, forStaff: true);
        if (!verdict.Ok)
            return await ConflictAsync(shop, order.Id, OrderConflictCode.PickupTimeUnavailable, verdict.ProblemText!, null, ct);

        var newKind = verdict.EndUtc is null ? PickupKind.Asap : PickupKind.Slot;
        var unchanged = newKind == order.PickupKind && verdict.PickupDate == order.PickupDate &&
                        (newKind == PickupKind.Asap || (verdict.StartUtc == order.PickupStartUtc && verdict.EndUtc == order.PickupEndUtc));
        if (unchanged)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new OrderActionResult(null, await staffDtos.BuildAsync(await LoadWithEventsAsync(order.Id, ct), shop, ct));
        }

        var before = new PickupSide(order.PickupKind, order.PickupDate, order.PickupStartUtc, order.PickupEndUtc, order.Number);
        if (verdict.PickupDate != order.PickupDate)
            order.Number = await numberAllocator.NextAsync(shop.Id, verdict.PickupDate, ct);
        order.PickupKind = newKind;
        order.PickupDate = verdict.PickupDate;
        order.PickupStartUtc = verdict.StartUtc;
        order.PickupEndUtc = verdict.EndUtc;
        order.UpdatedAtUtc = now;
        order.Version++;
        var after = new PickupSide(order.PickupKind, order.PickupDate, order.PickupStartUtc, order.PickupEndUtc, order.Number);
        await eventLog.AppendAsync(order, OrderEventKind.PickupChanged, await actorResolver.ResolveStaffAsync(user), order.Status, order.Status,
            comment: comment, changesJson: OrderChangeLog.SerializePickup(new PickupChangeLog(before, after)));

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new OrderActionResult(Conflict(OrderConflictCode.VersionMismatch, OrderTexts.VersionMismatch, null,
                await staffDtos.BuildAsync(await LoadWithEventsAsync(orderId, ct), shop, ct)));
        }
        db.ChangeTracker.Clear();
        return new OrderActionResult(null, await staffDtos.BuildAsync(await LoadWithEventsAsync(orderId, ct), shop, ct));
    }

    private static OrderProblemDto StockProblem(Product product, int maxQuantity, int? stepGrams)
    {
        // What the line can grow to: whole steps for a weight, whole pieces otherwise — never a number staff cannot enter.
        var available = product.Unit == ProductUnit.Weight
            ? maxQuantity / OrderQuantityRules.EffectiveStep(stepGrams) * OrderQuantityRules.EffectiveStep(stepGrams)
            : maxQuantity;
        return new OrderProblemDto(product.Id, product.Name, OrderProblemReason.InsufficientStock,
            OrderTexts.OnlyLeft(product.Unit, available), AvailableQuantity: available);
    }

    private static OrderActionResult Bad(string message) => new(new BadRequestObjectResult(message));

    private async Task<Order> LoadWithEventsAsync(Guid orderId, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery().FirstAsync(o => o.Id == orderId, ct);

    private async Task<OrderActionResult> ConflictAsync(
        Company shop, Guid orderId, OrderConflictCode code, string message, List<OrderProblemDto>? problems, CancellationToken ct)
    {
        // The body carries the CURRENT order (with its journal), reloaded clean — not the tracked graph this request touched.
        db.ChangeTracker.Clear();
        return new OrderActionResult(Conflict(code, message, problems, await staffDtos.BuildAsync(await LoadWithEventsAsync(orderId, ct), shop, ct)));
    }

    private static ConflictObjectResult Conflict(OrderConflictCode code, string message, List<OrderProblemDto>? problems, StaffOrderDto? order) =>
        new(new OrderConflictDto(code, message, Order: order, Problems: problems));
}
