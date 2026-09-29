using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §414 — the order as the customer reaches it: the page by its secret link, the cancellation by the
/// same link (no sign-in: whoever knows the 256-bit link may cancel — §405 deviation 4; the guard is the token and the order's
/// own <c>AllowCustomerCancel</c> snapshot), and "my orders". An unknown or malformed token is a bare 404, indistinguishable
/// from "there was no such order".
/// </summary>
public class PublicOrderService(
    AppDbContext db, OrderDtoMapper mapper, OrderEventLog eventLog, OrderActorResolver actorResolver, PublicSiteLinks links,
    CustomerOrderNotificationsBuilder notificationsBuilder)
{
    public async Task<PublicOrderDto?> GetAsync(string token, CancellationToken ct)
    {
        if (!PublicOrderToken.IsWellFormed(token)) return null;
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.PublicToken == token, ct);
        return order is null ? null : await ToPublicAsync(order, ct);
    }

    /// <summary>
    /// Cancellation by the customer (§414.2). The status is re-read and re-decided on every attempt: if staff moved the order to Ready
    /// (or changed anything) between the read and the write, the concurrency token fails the write and the decision is made again
    /// on the fresh state — so a cancel can never overwrite a status it did not see.
    /// </summary>
    public async Task<(ActionResult? Error, PublicOrderDto? Order)> CancelAsync(string token, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!PublicOrderToken.IsWellFormed(token)) return (new NotFoundResult(), null);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var order = await db.Orders.Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
                .FirstOrDefaultAsync(o => o.PublicToken == token, ct);
            if (order is null) return (new NotFoundResult(), null);

            // The order's own snapshot decides: not allowed at creation → never; Ready → "already collected"; terminal → not possible.
            if (!order.AllowCustomerCancelSnapshot)
                return await ConflictAsync(order, OrderConflictCode.CancelNotAllowed, OrderTexts.CancelNotAllowed, ct);
            if (order.Status == OrderStatus.Ready)
                return await ConflictAsync(order, OrderConflictCode.AlreadyReady, OrderTexts.AlreadyReady, ct);
            if (!OrderStateMachine.CanCustomerCancel(order.Status, order.AllowCustomerCancelSnapshot))
                return await ConflictAsync(order, OrderConflictCode.CancelNotAllowed, OrderTexts.CancelNotAllowed, ct);

            var from = order.Status;
            var now = DateTime.UtcNow;
            order.Status = OrderStatus.CancelledByCustomer;
            order.CompletedAtUtc = now;
            order.UpdatedAtUtc = now;
            order.Version++;
            var actor = await actorResolver.ResolveCustomerAsync(user, order.CustomerUserId, order.CustomerName);
            await eventLog.AppendAsync(order, OrderEventKind.CancelledByCustomer, actor, from, OrderStatus.CancelledByCustomer);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                continue; // somebody changed the order first — decide again on the fresh state
            }
            return (null, await ToPublicAsync(order, ct));
        }

        // Three lost races in a row: report the current state instead of looping forever.
        db.ChangeTracker.Clear();
        var latest = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.PublicToken == token, ct);
        return latest is null
            ? (new NotFoundResult(), null)
            : await ConflictAsync(latest, OrderConflictCode.CancelNotAllowed, OrderTexts.CancelNotAllowed, ct);
    }

    /// <summary>§414.3 — the caller's own orders: active ones (newest first), then finished ones of the last 30 days. Guest orders on the same number are NOT pulled in.</summary>
    public async Task<List<MyOrderSummaryDto>> ListMineAsync(string userId, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerUserId == userId &&
                        (o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready ||
                         o.CompletedAtUtc >= since))
            .Select(o => new
            {
                o.PublicToken, o.Number, o.BusinessDate, o.Status, o.EstimatedTotal, o.FinalTotal, o.HasWeightItems,
                o.CreatedAtUtc, o.CompletedAtUtc, o.PickupKind, o.PickupDate, o.PickupStartUtc, o.PickupEndUtc,
                ShopName = db.Companies.Where(c => c.Id == o.CompanyId).Select(c => c.Name).FirstOrDefault(),
                TimeZoneId = db.Companies.Where(c => c.Id == o.CompanyId).Select(c => c.TimeZoneId).FirstOrDefault()
            })
            .ToListAsync(ct);
        var now = DateTime.UtcNow;

        return rows
            .OrderBy(r => OrderStateMachine.IsActive(r.Status) ? 0 : 1)
            .ThenByDescending(r => OrderStateMachine.IsActive(r.Status) ? r.CreatedAtUtc : r.CompletedAtUtc ?? r.CreatedAtUtc)
            .Take(200)
            .Select(r => new MyOrderSummaryDto(
                links.OrderPageUrl(r.PublicToken), r.PublicToken, r.Number, r.BusinessDate, r.ShopName ?? string.Empty, r.Status,
                OrderTexts.StatusText(r.Status), r.Status == OrderStatus.Issued ? r.FinalTotal ?? r.EstimatedTotal : r.EstimatedTotal,
                r.HasWeightItems && r.Status != OrderStatus.Issued, r.CreatedAtUtc, OrderStateMachine.IsActive(r.Status),
                OrderDtoMapper.ToPickup(r.PickupKind, r.PickupDate, r.PickupStartUtc, r.PickupEndUtc, r.Status,
                    new OrderPickupContext(TimeZoneInfo.FindSystemTimeZoneById(r.TimeZoneId ?? "Europe/Moscow"), now))))
            .ToList();
    }

    private async Task<PublicOrderDto> ToPublicAsync(Order order, CancellationToken ct)
    {
        var shop = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == order.CompanyId, ct);
        var cityName = shop.CityId is null ? null : await db.Cities.AsNoTracking().Where(c => c.Id == shop.CityId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings { CompanyId = shop.Id };
        return mapper.ToPublic(order, shop, cityName, notificationsBuilder.Build(order, settings));
    }

    private async Task<(ActionResult? Error, PublicOrderDto? Order)> ConflictAsync(
        Order order, OrderConflictCode code, string message, CancellationToken ct) =>
        (new ConflictObjectResult(new OrderConflictDto(code, message, PublicOrder: await ToPublicAsync(order, ct))), null);
}
