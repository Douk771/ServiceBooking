using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceBooking.API.DTOs.Catalog;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §505.3, API_CONTRACT_CYCLE25.md §531 — the public catalog of shops on the goods home page. Anonymous, rate-limited per IP
/// (<c>goods-catalog</c>, 120/min). An unknown city is an empty page, not a 404 (the <c>companies/public</c> convention); an unparsable page is page 1.
/// </summary>
[ApiController]
[Route("api/goods/catalog")]
[AllowAnonymous]
public class GoodsCatalogController(GoodsCatalogService catalog) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("goods-catalog")]
    public async Task<ActionResult<GoodsCatalogPageDto>> Get(
        [FromQuery] int? cityId, [FromQuery] bool openNow, [FromQuery] string? search, [FromQuery] string? page, CancellationToken ct)
    {
        int? pageNumber = int.TryParse(page, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null;
        return Ok(await catalog.GetAsync(cityId, openNow, search, pageNumber, ct));
    }
}
