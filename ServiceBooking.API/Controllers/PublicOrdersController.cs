using System.Security.Claims;
using ServiceBooking.API.Services.Notifications.WebPush;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceBooking.API.DTOs.Orders;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §414 — the order as the customer sees it: by the secret link (page, cancellation) and "my orders".
/// The token is a 256-bit secret in the path; the serilog request path is masked for it (LoggingExtensions) and so is nginx's log.
/// </summary>
[ApiController]
[Route("api/orders")]
public class PublicOrdersController(
    AppDbContext db, PublicOrderService orders, OrderPushSubscriptionWriter pushWriter, CustomerOrderNotificationsBuilder pushInfo) : ControllerBase
{
    public const string ShopDoesNotPush = "Магазин не отправляет уведомления в браузер";
    public const string OrderFinished = "Заказ уже завершён — уведомления по нему не приходят";
    public const string PlatformPushOff = "Уведомления в браузере пока не включены на платформе.";

    /// <summary>
    /// ARCHITECTURE_CYCLE24.md §456.1 — "notify me about this order in this browser". Anonymous: the secret token in the path is the only credential. An unknown
    /// token is a bare 404 (no oracle); then the body (400, the same sentences as <c>POST /api/push/subscriptions</c>); then the 409s — the shop switched web-push
    /// off, the order is final, the platform has push off. 201 on a new subscription, 200 when the same endpoint was already subscribed (keys refreshed).
    /// </summary>
    [HttpPost("public/{token}/push-subscription")]
    [EnableRateLimiting("order-push")]
    public async Task<ActionResult<OrderPushStateDto>> SubscribePush(string token, OrderPushSubscribeInput input, CancellationToken ct)
    {
        var order = await FindByTokenAsync(token, ct);
        if (order is null) return NotFound();

        if (!PushEndpointValidator.IsValid(input.Endpoint))
            return BadRequest("Некорректный адрес подписки (endpoint).");
        if (input.Keys is null || string.IsNullOrWhiteSpace(input.Keys.P256dh) || input.Keys.P256dh.Length > 200)
            return BadRequest("Некорректный ключ подписки (p256dh).");
        if (string.IsNullOrWhiteSpace(input.Keys.Auth) || input.Keys.Auth.Length > 100)
            return BadRequest("Некорректный ключ подписки (auth).");
        if (input.DeviceLabel is { Length: > 100 }) return BadRequest("Слишком длинное название устройства.");

        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == order.CompanyId, ct) ?? new ShopSettings { CompanyId = order.CompanyId };
        if (!settings.CustomerWebPushEnabled) return Conflict(ShopDoesNotPush);
        if (OrderStateMachine.IsTerminal(order.Status)) return Conflict(OrderFinished);
        if (!pushInfo.PlatformPushEnabled) return Conflict(PlatformPushOff);

        var (_, created, count) = await pushWriter.UpsertAsync(order.Id, input.Endpoint, input.Keys.P256dh, input.Keys.Auth, ct);
        var state = new OrderPushStateDto(true, count);
        return created ? StatusCode(StatusCodes.Status201Created, state) : Ok(state);
    }

    /// <summary>Stops the notifications of this browser for this order. POST, not DELETE with a body: proxies and OpenAPI linters drop the body of a DELETE. Idempotent — 204 even when there was nothing to remove.</summary>
    [HttpPost("public/{token}/push-subscription/remove")]
    [EnableRateLimiting("order-push")]
    public async Task<IActionResult> UnsubscribePush(string token, OrderPushUnsubscribeInput input, CancellationToken ct)
    {
        var order = await FindByTokenAsync(token, ct);
        if (order is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Endpoint) || input.Endpoint.Length > 500) return BadRequest("Некорректный адрес подписки (endpoint).");
        await pushWriter.RemoveAsync(order.Id, input.Endpoint, ct);
        return NoContent();
    }

    private async Task<Order?> FindByTokenAsync(string token, CancellationToken ct) =>
        PublicOrderToken.IsWellFormed(token) ? await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.PublicToken == token, ct) : null;

    /// <summary>The order page — polled by the frontend every 10 s until the status is terminal. Unknown token → bare 404.</summary>
    [HttpGet("public/{token}")]
    [EnableRateLimiting("order-public")]
    public async Task<ActionResult<PublicOrderDto>> Get(string token, CancellationToken ct)
    {
        var order = await orders.GetAsync(token, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost("public/{token}/cancel")]
    [EnableRateLimiting("order-public")]
    public async Task<ActionResult<PublicOrderDto>> Cancel(string token, CancellationToken ct)
    {
        var (error, order) = await orders.CancelAsync(token, User, ct);
        return error is not null ? error : Ok(order);
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<MyOrderSummaryDto>>> ListMine(CancellationToken ct) =>
        Ok(await orders.ListMineAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct));
}
