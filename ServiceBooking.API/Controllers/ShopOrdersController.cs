using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396–§397, API_CONTRACT_CYCLE23.md §415–§418 — the orders screen of a shop: the board (polled every 5 s),
/// the order card with its journal, the status transitions, editing and the issue. Available to the owner and to staff (Master) alike
/// (§392.2). Every action carries <c>expectedVersion</c>; on a conflict the body holds the CURRENT order and the action was not applied.
/// Order of checks: token → the shop exists and is a shop (404) → the role (403) → the order of this shop (404) → conflicts (409, JSON).
/// </summary>
[ApiController]
[Route("api/shops/{shopId:guid}")]
[Authorize]
public class ShopOrdersController(
    AppDbContext db, ShopAccessResolver access, OrderTransitionService transitions, OrderEditService editing) : ControllerBase
{
    private const int CompletedTodayLimit = 500;

    /// <summary>
    /// The board (§397.1). The cheap path: one primary-key read of the revision counter; if it and the business day match what the client
    /// sent, the answer is <c>changed:false</c> with no arrays. Otherwise the full board: active orders (oldest first) and today's finished ones.
    /// The revision is read BEFORE the data, so a change committed in between is picked up by the next poll (a newer revision), never missed.
    /// </summary>
    [HttpGet("order-board")]
    [EnableRateLimiting("order-board")]
    public async Task<ActionResult<OrderBoardDto>> GetBoard(
        Guid shopId, [FromQuery] long? sinceRevision, [FromQuery] DateOnly? businessDate, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var shop = result.Shop!;

        var revision = await db.ShopSettings.AsNoTracking().Where(s => s.CompanyId == shopId)
            .Select(s => (long?)s.OrdersRevision).FirstOrDefaultAsync(ct) ?? 0;
        var nowUtc = DateTime.UtcNow;
        var today = ShopClock.BusinessDate(shop.TimeZoneId, nowUtc);
        if (sinceRevision == revision && businessDate == today)
            return Ok(new OrderBoardDto(revision, false, today, nowUtc, null, null, null, null));

        var active = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CompanyId == shopId && (o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready))
            .OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Number).ToListAsync(ct);
        var (dayStart, dayEnd) = ShopClock.DayBoundsUtc(shop.TimeZoneId, today);
        var completed = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CompanyId == shopId && o.CompletedAtUtc >= dayStart && o.CompletedAtUtc < dayEnd &&
                        o.Status != OrderStatus.New && o.Status != OrderStatus.Accepted && o.Status != OrderStatus.Ready)
            .OrderByDescending(o => o.CompletedAtUtc).Take(CompletedTodayLimit).ToListAsync(ct);

        List<StaffOrderCardDto> Cards(OrderStatus status) => active.Where(o => o.Status == status).Select(OrderDtoMapper.ToCard).ToList();
        return Ok(new OrderBoardDto(
            revision, true, today, nowUtc, Cards(OrderStatus.New), Cards(OrderStatus.Accepted), Cards(OrderStatus.Ready),
            completed.Select(OrderDtoMapper.ToCard).ToList()));
    }

    /// <summary>The order card with its journal (§396.6). An order of another shop is a 404.</summary>
    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult<StaffOrderDto>> GetOrder(Guid shopId, Guid orderId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == shopId, ct);
        return order is null ? NotFound() : Ok(OrderDtoMapper.ToStaff(order));
    }

    [HttpPost("orders/{orderId:guid}/accept")]
    public Task<ActionResult<StaffOrderDto>> Accept(Guid shopId, Guid orderId, VersionInput input, CancellationToken ct) =>
        ActAsync(shopId, orderId, OrderAction.Accept, input.ExpectedVersion, null, ct);

    [HttpPost("orders/{orderId:guid}/reject")]
    public Task<ActionResult<StaffOrderDto>> Reject(Guid shopId, Guid orderId, ReasonInput input, CancellationToken ct) =>
        ActAsync(shopId, orderId, OrderAction.Reject, input.ExpectedVersion, input.Reason, ct);

    [HttpPost("orders/{orderId:guid}/ready")]
    public Task<ActionResult<StaffOrderDto>> MarkReady(Guid shopId, Guid orderId, VersionInput input, CancellationToken ct) =>
        ActAsync(shopId, orderId, OrderAction.MarkReady, input.ExpectedVersion, null, ct);

    [HttpPost("orders/{orderId:guid}/not-picked-up")]
    public Task<ActionResult<StaffOrderDto>> NotPickedUp(Guid shopId, Guid orderId, VersionInput input, CancellationToken ct) =>
        ActAsync(shopId, orderId, OrderAction.NotPickedUp, input.ExpectedVersion, null, ct);

    [HttpPost("orders/{orderId:guid}/cancel")]
    public Task<ActionResult<StaffOrderDto>> Cancel(Guid shopId, Guid orderId, ReasonInput input, CancellationToken ct) =>
        ActAsync(shopId, orderId, OrderAction.Cancel, input.ExpectedVersion, input.Reason, ct);

    /// <summary>The amount to pay for the actual weights (§418) — the same arithmetic the issue stores; changes nothing.</summary>
    [HttpPost("orders/{orderId:guid}/issue-quote")]
    public async Task<ActionResult<IssueQuoteDto>> QuoteIssue(Guid shopId, Guid orderId, IssueQuoteInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var (error, quote) = await transitions.QuoteIssueAsync(result.Shop!, orderId, input.ActualQuantities, ct);
        return error is not null ? error : Ok(quote);
    }

    [HttpPost("orders/{orderId:guid}/issue")]
    public async Task<ActionResult<StaffOrderDto>> Issue(Guid shopId, Guid orderId, IssueInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var outcome = await transitions.IssueAsync(result.Shop!, orderId, input.ExpectedVersion, input.ActualQuantities, User, ct);
        return outcome.Error is not null ? outcome.Error : Ok(outcome.Order);
    }

    /// <summary>Edit the content of a live order (§417): the FULL desired content, new lines at the current catalog price.</summary>
    [HttpPut("orders/{orderId:guid}/items")]
    public async Task<ActionResult<StaffOrderDto>> EditItems(Guid shopId, Guid orderId, EditOrderInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var outcome = await editing.EditAsync(result.Shop!, orderId, input, User, ct);
        return outcome.Error is not null ? outcome.Error : Ok(outcome.Order);
    }

    private async Task<ActionResult<StaffOrderDto>> ActAsync(
        Guid shopId, Guid orderId, OrderAction action, int expectedVersion, string? reason, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var outcome = await transitions.ApplyAsync(result.Shop!, orderId, action, expectedVersion, reason, User, ct);
        return outcome.Error is not null ? outcome.Error : Ok(outcome.Order);
    }
}
