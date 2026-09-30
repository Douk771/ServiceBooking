using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
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
    AppDbContext db, ShopAccessResolver access, OrderTransitionService transitions, OrderEditService editing,
    StaffOrderDtoFactory staffDtos, Services.Shops.ShopGateLoader gates) : ControllerBase
{
    private const int CompletedTodayLimit = 500;

    /// <summary>
    /// The board (§397.1, cycle 24: §453). The cheap path: one primary-key read of the settings row — the revision counter AND the acceptance state (pause /
    /// stop live on the same row, so the state rides in EVERY answer at no extra query); if the revision and the business day match what the client sent, the
    /// answer is <c>changed:false</c> with no arrays. Otherwise the full board: active orders ordered by pickup time, pre-orders grouped by date, and today's
    /// finished ones. A pre-order becomes an ordinary "accepted" one on its pickup day by itself — the business day changes, the poll is "changed", no task.
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

        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shopId, ct) ?? new ShopSettings { CompanyId = shopId };
        var revision = settings.OrdersRevision;
        var nowUtc = DateTime.UtcNow;
        var pickupContext = await gates.PickupContextAsync(shop, settings, nowUtc, ct);
        // The storefront's "today": the current WORKING day (in the after-midnight tail of an overnight interval it is yesterday's date).
        var today = pickupContext.WorkingDay;
        var acceptance = ShopScheduleMapper.ToDto(ShopAcceptanceRules.State(settings, nowUtc, pickupContext.Zone));
        if (sinceRevision == revision && businessDate == today)
            return Ok(new OrderBoardDto(revision, false, today, nowUtc, acceptance, null, null, null, null, null));

        var active = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CompanyId == shopId && (o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready))
            .OrderBy(o => o.PickupStartUtc).ThenBy(o => o.CreatedAtUtc).ThenBy(o => o.Number).ToListAsync(ct);
        var (dayStart, dayEnd) = ShopClock.DayBoundsUtc(shop.TimeZoneId, today);
        if (dayEnd <= nowUtc) dayEnd = nowUtc.AddSeconds(1); // the tail after midnight still belongs to the working day
        var completed = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CompanyId == shopId && o.CompletedAtUtc >= dayStart && o.CompletedAtUtc < dayEnd &&
                        o.Status != OrderStatus.New && o.Status != OrderStatus.Accepted && o.Status != OrderStatus.Ready)
            .OrderByDescending(o => o.CompletedAtUtc).Take(CompletedTodayLimit).ToListAsync(ct);

        // One batch of cards (one query for the messenger statuses), then split into the columns.
        var cards = (await staffDtos.BuildCardsAsync([.. active, .. completed], pickupContext, ct)).ToDictionary(c => c.Id);
        List<StaffOrderCardDto> Column(IEnumerable<Order> orders) => orders.Select(o => cards[o.Id]).ToList();

        var preorders = active.Where(o => o.Status == OrderStatus.Accepted && o.PickupDate > today)
            .GroupBy(o => o.PickupDate).OrderBy(g => g.Key)
            .Select(g => new PreorderGroupDto(g.Key, ShopTimeTexts.DateShort(g.Key), Column(g))).ToList();
        return Ok(new OrderBoardDto(
            revision, true, today, nowUtc, acceptance,
            Column(active.Where(o => o.Status == OrderStatus.New)),
            Column(active.Where(o => o.Status == OrderStatus.Accepted && o.PickupDate <= today)),
            Column(active.Where(o => o.Status == OrderStatus.Ready)),
            preorders, Column(completed)));
    }

    /// <summary>The order card with its journal (§396.6). An order of another shop is a 404.</summary>
    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult<StaffOrderDto>> GetOrder(Guid shopId, Guid orderId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == shopId, ct);
        return order is null ? NotFound() : Ok(await staffDtos.BuildAsync(order, result.Shop!, ct));
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

    /// <summary>US-24-09 (P1) — staff move the pickup time (and maybe the pickup day, which gives the order a new number). Only New / Accepted (§481).</summary>
    [HttpPut("orders/{orderId:guid}/pickup")]
    public async Task<ActionResult<StaffOrderDto>> ChangePickup(Guid shopId, Guid orderId, ChangePickupInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var outcome = await editing.ChangePickupAsync(result.Shop!, orderId, input, User, ct);
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
