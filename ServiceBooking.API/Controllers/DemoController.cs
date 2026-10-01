using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Demo;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.4, API_CONTRACT_CYCLE28.md §597–§598 — the two routes of the demo stand. Both are <see cref="DemoOnlyAttribute"/>: on a production
/// configuration they answer 404 with an empty body before any action code runs, as if they did not exist (the auth and route-table tests check it).
/// </summary>
[ApiController]
[Route("api/demo")]
[DemoOnly]
public class DemoController(
    IOptions<DemoModeOptions> options, DemoMaintenanceFlag maintenanceFlag, DemoLoginService loginService, PublicSiteLinks siteLinks, AppDbContext db) : ControllerBase
{
    /// <summary>Status of the demo: the caption of the banner's data (reset time), the role buttons and "reset in progress". Anonymous; answers 200 even while the
    /// reset runs (the maintenance middleware lets this one route through). The optional <c>product</c> query (API_CONTRACT_CYCLE35.md §35.21) picks the three roles
    /// of "Запись" (default, the answer of cycle 28) or of "Заказы"; any other value is a 400. The order of checks: demo mode ([DemoOnly], 404) → product → 200.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<DemoStatusDto>> GetStatus([FromQuery] string? product, CancellationToken ct)
    {
        if (!ShowcaseDemoRoles.TryParseProduct(product, out var demoProduct))
            return BadRequest("Параметр product должен быть services или orders.");

        var settings = options.Value;
        var raw = await db.PlatformSettings.AsNoTracking()
            .Where(s => s.Key == DemoCatalog.LastResetKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        DateTime? lastReset = DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

        return Ok(new DemoStatusDto(
            DemoMode: true,
            Resetting: maintenanceFlag.IsResetting(DateTime.UtcNow),
            ResetLocalTime: settings.ResetLocalTime,
            TimeZoneId: settings.TimeZoneId,
            LastResetAtUtc: lastReset,
            Roles: ShowcaseDemoRoles.ForProduct(demoProduct).Select(r => new DemoRoleDto(r.Role, r.Label)).ToList(),
            SiteUrls: new DemoSiteUrlsDto(siteLinks.SiteBaseUrl(CompanyKind.Services), siteLinks.SiteBaseUrl(CompanyKind.Orders))));
    }

    /// <summary>Signs in as a ready demo role without a password. 400 for an unknown role, 409 while the demo data has not been created yet.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("demo-login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] DemoLoginRequest? request)
    {
        var result = await loginService.LoginAsync(request?.Role);
        return result.Status switch
        {
            DemoLoginStatus.Ok => Ok(result.Response),
            DemoLoginStatus.NotSeeded => Conflict("Демо-данные ещё не созданы. Зайдите чуть позже."),
            _ => BadRequest("Неизвестная демо-роль."),
        };
    }
}
