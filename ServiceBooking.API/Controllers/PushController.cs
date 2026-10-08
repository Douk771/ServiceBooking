using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Web Push мастеру — subscription lifecycle and platform config (ARCHITECTURE_CYCLE9.md §105.5,
/// contracts/cycle9/openapi.yaml <c>/push/**</c>, US-116, US-118, US-123). Every read/write here is
/// scoped to the CALLER's own <c>UserId</c> — there is no "list someone else's devices" shape at all.
/// </summary>
[ApiController]
[Route("api/push")]
[Authorize]
public class PushController(
    AppDbContext db, PushSubscriptionWriter writer, IOptions<WebPushOptions> webPushOptions, PublicSiteLinks siteLinks) : ControllerBase
{
    [HttpGet("config")]
    public async Task<ActionResult<PushConfigDto>> GetConfig([FromQuery] string? site, [FromQuery] bool allSites, CancellationToken ct)
    {
        if (!CompanyKindQuery.TryParse(site, out var siteKind)) return BadRequest(CompanyKindQuery.UnknownKindText);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var opts = webPushOptions.Value;
        // §105.3: enabled=false means Notifications:StaffPush:Provider=logging — a normal, documented
        // state (SPEC П13), not a failure. publicKey is null in that state (nothing to hand the browser).
        var enabled = string.Equals(opts.Provider, "web-push", StringComparison.OrdinalIgnoreCase);

        var memberships = await db.CompanyMembers.AsNoTracking()
            .Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.UserId == userId)
            .Select(cm => cm.CompanyId).Distinct().ToListAsync(ct);

        // ARCHITECTURE_CYCLE24.md §484: only the companies of THIS site — the shops that leaked into the ezbook list in cycle 23 are gone from it.
        var companies = await db.Companies.AsNoTracking()
            .Where(c => memberships.Contains(c.Id) && (allSites || c.Kind == siteKind))
            .Select(c => new { c.Id, c.Name, c.Kind }).ToListAsync(ct);
        // §33.21: allSites — Services first, then Orders, then Stays (cycle 37), inside by name (ordinal).
        companies = companies.OrderBy(c => c.Kind).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        var settingsByCompany = await db.CompanyNotificationSettings.AsNoTracking()
            .Where(s => memberships.Contains(s.CompanyId))
            .ToDictionaryAsync(s => s.CompanyId, s => s.StaffPushEnabled, ct);

        var companyDtos = companies.Select(c => new PushConfigCompanyDto(
            c.Id, c.Name, settingsByCompany.TryGetValue(c.Id, out var v) ? v : new CompanyNotificationSettings().StaffPushEnabled, c.Kind)).ToList();

        return Ok(new PushConfigDto(enabled, enabled ? opts.VapidPublicKey : null, opts.MaxSubscriptionsPerUser, companyDtos, siteKind,
            new PushSiteUrlsDto(siteLinks.SiteBaseUrl(CompanyKind.Services), siteLinks.SiteBaseUrl(CompanyKind.Orders), siteLinks.SiteBaseUrl(CompanyKind.Stays))));
    }

    [HttpGet("subscriptions")]
    public async Task<ActionResult<PushSubscriptionListDto>> ListSubscriptions([FromQuery] string? currentEndpoint, [FromQuery] string? site, [FromQuery] bool allSites)
    {
        if (!CompanyKindQuery.TryParse(site, out var siteKind)) return BadRequest(CompanyKindQuery.UnknownKindText);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var rows = await writer.ListAsync(userId, HttpContext.RequestAborted, siteKind, allSites);

        var items = rows.Select(s => new PushSubscriptionDto(
            s.Id, s.DeviceLabel, s.CreatedAtUtc, s.LastSuccessAtUtc,
            // §105.5: isCurrent is computed by the SERVER, comparing against currentEndpoint from the
            // query — with no parameter, every row is isCurrent=false (never guessed).
            !string.IsNullOrEmpty(currentEndpoint) && string.Equals(s.Endpoint, currentEndpoint, StringComparison.Ordinal)
            && s.Site == siteKind,
            s.Site)).ToList();

        return Ok(new PushSubscriptionListDto(items));
    }

    [HttpPost("subscriptions")]
    [EnableRateLimiting("push-subscribe")]
    public async Task<ActionResult<PushSubscriptionDto>> CreateSubscription([FromBody] CreatePushSubscriptionInput dto)
    {
        var opts = webPushOptions.Value;
        if (!string.Equals(opts.Provider, "web-push", StringComparison.OrdinalIgnoreCase))
            return Conflict("Уведомления на устройство пока не включены на платформе.");

        if (!PushEndpointValidator.IsValid(dto.Endpoint))
            return BadRequest("Некорректный адрес подписки (endpoint).");
        if (dto.Keys is null || string.IsNullOrWhiteSpace(dto.Keys.P256dh) || dto.Keys.P256dh.Length > 200)
            return BadRequest("Некорректный ключ подписки (p256dh).");
        if (string.IsNullOrWhiteSpace(dto.Keys.Auth) || dto.Keys.Auth.Length > 100)
            return BadRequest("Некорректный ключ подписки (auth).");
        if (dto.DeviceLabel is { Length: > 100 })
            return BadRequest("Слишком длинное название устройства.");

        if (dto.Site is { } requestedSite && !Enum.IsDefined(requestedSite)) return BadRequest(CompanyKindQuery.UnknownKindText);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (subscription, created) = await writer.UpsertAsync(
            userId, dto.Endpoint, dto.Keys.P256dh, dto.Keys.Auth, dto.DeviceLabel, HttpContext.RequestAborted, dto.Site ?? CompanyKind.Services);

        var result = new PushSubscriptionDto(subscription.Id, subscription.DeviceLabel, subscription.CreatedAtUtc,
            subscription.LastSuccessAtUtc, IsCurrent: true, subscription.Site);
        return created
            ? CreatedAtAction(nameof(ListSubscriptions), null, result)
            : Ok(result);
    }

    [HttpDelete("subscriptions/current")]
    public async Task<IActionResult> DeleteCurrentSubscription([FromBody] DeleteCurrentPushSubscriptionInput dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Endpoint) || dto.Endpoint.Length > 500)
            return BadRequest("Некорректный адрес подписки (endpoint).");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // §105.5 rubeж 2: idempotent — 204 even if the row is already gone.
        await writer.DeleteByEndpointAsync(userId, dto.Endpoint, HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpDelete("subscriptions/{id:guid}")]
    public async Task<IActionResult> DeleteSubscription(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var found = await writer.DeleteByIdAsync(userId, id, HttpContext.RequestAborted);
        // §105.5: a subscription belonging to someone else is 404, never 403 — its existence is not
        // confirmed either way.
        return found ? NoContent() : NotFound();
    }
}
