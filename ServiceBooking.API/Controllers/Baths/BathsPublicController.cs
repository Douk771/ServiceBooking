using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.Services.Baths;

namespace ServiceBooking.API.Controllers.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.10.2, §42.10.3, API_CONTRACT_CYCLE42.md §42.22, §42.23 — the anonymous catalog of «Бани», its cities and the page of a complex. The answers carry no
/// occupied intervals, names or requisites. A company of another kind, a missing or a blocked one is a 404 with an empty body.
/// </summary>
[ApiController]
[Route("api/baths")]
public class BathsPublicController(BathsCatalogService catalog) : ControllerBase
{
    [HttpGet("catalog")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<BathsCatalogPageDto>> Catalog(
        [FromQuery] int? cityId, [FromQuery] string? date, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var (error, result) = await catalog.CatalogAsync(cityId, date, page, pageSize, ct);
        return error ?? Ok(result);
    }

    [HttpGet("catalog/cities")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<BathsCatalogCitiesDto>> Cities(CancellationToken ct) => Ok(await catalog.CitiesAsync(ct));

    [HttpGet("public/companies/{slug}")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<BathsPublicCompanyDto>> Company(string slug, CancellationToken ct)
    {
        var page = await catalog.CompanyPageAsync(slug, ct);
        return page is null ? NotFound() : Ok(page);
    }
}
