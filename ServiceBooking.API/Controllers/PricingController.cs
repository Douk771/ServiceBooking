using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.API.Controllers;

/// <summary>API_CONTRACT_CYCLE5.md §48, contracts/openapi-cycle5.yaml paths /pricing and
/// /admin/pricing/preview. GET /api/pricing is the only anonymous endpoint of cycle 5.</summary>
[ApiController]
public class PricingController(PricingCatalogCache cache) : ControllerBase
{
    // Anonymous. 404 (empty body) while the superadmin hasn't flipped pricing.public-enabled — never
    // 403 and never an empty array, so an outside caller can't tell the catalog exists at all (§48).
    // Not gated by LegalConsentFilter: that filter only runs for authenticated callers, so an anonymous
    // GET here is unaffected by it regardless of allow-listing.
    [HttpGet("api/pricing")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicPricing(CancellationToken ct)
    {
        if (!await cache.IsPublicEnabledAsync(ct)) return NotFound();

        var (dto, etag) = await cache.GetAsync(ct);

        if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
            ifNoneMatch.Any(v => v == etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "public, max-age=60";
        return Ok(dto);
    }

    // SuperAdmin. Same shape as GET /api/pricing but ignores the publication switch and never caches/
    // ETags — the admin/legal-review screen must always show the current draft, published or not.
    [HttpGet("api/admin/pricing/preview")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<PublicPricingDto>> GetPricingPreview(CancellationToken ct)
    {
        var (dto, _) = await cache.GetAsync(ct);
        return Ok(dto);
    }
}
