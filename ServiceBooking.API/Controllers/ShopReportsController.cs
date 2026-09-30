using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Orders.Reports;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §501–§503, API_CONTRACT_CYCLE25.md §526–§528 — the reports of a shop: the order history, the summary of a day / period and the
/// pick list. Order of checks (as in cycle 23): token → the company exists and is a shop (404) → the role (403, empty body) → validation (400, a Russian
/// string). "Today" is the shop's WORKING day; periods are computed by the server. The history is a POST because the customer filter may hold digits of a
/// phone number and query strings end up in access logs.
/// </summary>
[ApiController]
[Route("api/shops/{shopId:guid}")]
[Authorize]
public class ShopReportsController(ShopAccessResolver access, ShopReportService reports, PickListService pickList) : ControllerBase
{
    /// <summary>The history of orders with filters; owner and staff. Idempotent, changes nothing.</summary>
    [HttpPost("order-history")]
    [EnableRateLimiting("shop-reports")]
    public async Task<ActionResult<OrderHistoryPageDto>> History(Guid shopId, [FromBody] OrderHistoryQuery? query, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewOrderReports, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var (error, page) = await reports.HistoryAsync(result.Shop!, query ?? new OrderHistoryQuery(null, null, null, null, null, null, null, null, null, null), ct);
        return error is not null ? BadRequest(error) : Ok(page);
    }

    /// <summary>The summary of a day or period; the owner and SuperAdmin only (a staff member gets 403).</summary>
    [HttpGet("summary")]
    [EnableRateLimiting("shop-reports")]
    public async Task<ActionResult<ShopSummaryDto>> Summary(
        Guid shopId, [FromQuery] ReportPeriodPreset? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] SummaryTopSort? top, [FromQuery] bool compare, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewSummary, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var (error, summary) = await reports.SummaryAsync(result.Shop!, period, from, to, top, compare, ct);
        return error is not null ? BadRequest(error) : Ok(summary);
    }

    /// <summary>The pick list of a pickup day and interval; owner and staff. No customer name or phone in the answer.</summary>
    [HttpGet("picklist")]
    [EnableRateLimiting("shop-reports")]
    public async Task<ActionResult<PickListDto>> PickList(
        Guid shopId, [FromQuery] DateOnly? date, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] bool includeNew = true, CancellationToken ct = default)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewOrderReports, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var (error, list) = await pickList.BuildAsync(result.Shop!, date, from, to, includeNew, ct);
        return error is not null ? BadRequest(error) : Ok(list);
    }
}
