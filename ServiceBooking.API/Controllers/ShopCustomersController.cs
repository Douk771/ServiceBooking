using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Orders.Reports;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §504, API_CONTRACT_CYCLE25.md §529–§530 — the customer card of a shop and the shop's note about a customer. The customer is
/// addressed by <c>customerRef</c>, the id of any of their orders in THIS shop — the phone is never in a URL. Order of checks: token → the shop exists
/// and is a shop (404) → the role (403, empty body) → the ref (404, empty body: another shop's order, an erased one and an unknown one look the same).
/// Only the shop's staff see the card and the note; the customer and the platform administrator do not.
/// </summary>
[ApiController]
[Route("api/shops/{shopId:guid}/customers/{customerRef:guid}")]
[Authorize]
public class ShopCustomersController(ShopAccessResolver access, ShopCustomerService customers) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("shop-reports")]
    public async Task<ActionResult<ShopCustomerCardDto>> Card(Guid shopId, Guid customerRef, [FromQuery] int? page, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewOrderReports, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        if (page is < 1) return BadRequest(ShopReportService.PageText);

        var card = await customers.CardAsync(result.Shop!, customerRef, page ?? 1, ct);
        return card is null ? NotFound() : Ok(card);
    }

    [HttpGet("note")]
    [EnableRateLimiting("shop-reports")]
    public async Task<ActionResult<ShopCustomerNoteStateDto>> GetNote(Guid shopId, Guid customerRef, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewOrderReports, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var (found, note) = await customers.GetNoteAsync(result.Shop!, customerRef, ct);
        return found ? Ok(new ShopCustomerNoteStateDto(note)) : NotFound();
    }

    [HttpPut("note")]
    [EnableRateLimiting("shop-reports")]
    public async Task<ActionResult<ShopCustomerNoteStateDto>> PutNote(Guid shopId, Guid customerRef, [FromBody] ShopCustomerNoteInput? input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCustomerNotes, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var (found, error, note) = await customers.PutNoteAsync(result.Shop!, customerRef, input?.Text, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (!found) return NotFound();
        return error is not null ? BadRequest(error) : Ok(new ShopCustomerNoteStateDto(note));
    }
}
