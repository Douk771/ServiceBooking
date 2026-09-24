using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// <c>GET|PUT /api/companies/{companyId}/staff-push-settings</c> — whether staff get Web Push on new
/// bookings (ARCHITECTURE_CYCLE9.md §105.7, US-117). Deliberately its OWN controller/route, NOT folded
/// into <see cref="CompanyNotificationsController"/>'s <c>notification-settings</c>: that route answers
/// 402 to every owner today (<c>AllowNotificationChannel</c> is off on every tariff) and this one must
/// NEVER be tariff-gated (SPEC П6) — see <c>CompanyNotificationSettings.StaffPushEnabled</c>'s own doc
/// comment for the full "почему не бит в маске" reasoning.
/// </summary>
[ApiController]
[Route("api/companies/{companyId:guid}/staff-push-settings")]
[Authorize]
public class CompanyPushSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StaffPushSettingsDto>> Get(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Forbid();
        if (!await CompanyMembership.IsOwnerAsync(db, companyId, userId) && !User.IsInRole("SuperAdmin"))
            return Forbid();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        var settings = await db.CompanyNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);
        // §105.4/§105.7: no row = defaults (StaffPushEnabled = true) — no backfill needed for companies
        // that existed before this cycle.
        return Ok(new StaffPushSettingsDto(settings?.StaffPushEnabled ?? new CompanyNotificationSettings().StaffPushEnabled));
    }

    [HttpPut]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffPushSettingsDto>> Update(Guid companyId, [FromBody] StaffPushSettingsDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Forbid();
        // §105.7: owner and superadmin only — master gets an explicit Forbid, never a silently empty value.
        if (!await CompanyMembership.IsOwnerAsync(db, companyId, userId) && !User.IsInRole("SuperAdmin"))
            return Forbid();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        var settings = await db.CompanyNotificationSettings.FirstOrDefaultAsync(s => s.CompanyId == companyId);
        if (settings is null)
        {
            settings = new CompanyNotificationSettings { CompanyId = companyId };
            db.CompanyNotificationSettings.Add(settings);
        }

        // §105.7: turning this off holds delivery WITHOUT touching a single PushSubscription row —
        // StaffPushDispatchTask re-checks it at send time, so re-enabling resumes delivery with no
        // re-login and no re-subscription required (US-117).
        settings.StaffPushEnabled = dto.StaffPushEnabled;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = userId;

        await db.SaveChangesAsync();
        return Ok(new StaffPushSettingsDto(settings.StaffPushEnabled));
    }
}
