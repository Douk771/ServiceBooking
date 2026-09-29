using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Orders;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §414 — the order as the customer sees it: by the secret link (page, cancellation) and "my orders".
/// The token is a 256-bit secret in the path; the serilog request path is masked for it (LoggingExtensions) and so is nginx's log.
/// </summary>
[ApiController]
[Route("api/orders")]
public class PublicOrdersController(PublicOrderService orders) : ControllerBase
{
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
