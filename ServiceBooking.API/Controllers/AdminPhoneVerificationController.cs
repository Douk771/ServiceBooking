using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE12.md §152, API_CONTRACT_CYCLE12.md §172 (US-12-13). SuperAdmin-only, read via
/// <c>curl</c> per DEPLOY.md — this cycle deliberately does not add an admin SCREEN for it (§154, §158).
/// Every number here is a COUNT over <see cref="Core.Entities.PhoneVerificationSession"/> — no separate
/// counters table exists to drift out of sync with it.
/// </summary>
[ApiController]
[Route("api/admin/phone-verification")]
[Authorize(Roles = "SuperAdmin")]
public class AdminPhoneVerificationController(
    AppDbContext db,
    IPhoneVerificationMethodRegistry registry,
    PhoneVerificationDiagnostics diagnostics,
    IOptions<PhoneVerificationOptions> options) : ControllerBase
{
    [HttpGet("diagnostics")]
    public async Task<ActionResult<PhoneVerificationDiagnosticsDto>> GetDiagnostics(CancellationToken ct)
    {
        var adapter = registry.Get(PhoneVerificationMethod.MaxBot);
        var enabled = adapter.Enabled;
        var methods = enabled ? new[] { adapter.Method } : [];

        var since = DateTime.UtcNow.AddHours(-24);

        var started = await db.PhoneVerificationSessions.CountAsync(s => s.CreatedAtUtc >= since, ct);
        var verified = await db.PhoneVerificationSessions.CountAsync(s =>
            (s.Status == PhoneVerificationStatus.Verified || s.Status == PhoneVerificationStatus.Consumed) &&
            s.CompletedAtUtc >= since, ct);

        var rejectedByReasonRows = await db.PhoneVerificationSessions
            .Where(s => s.Status == PhoneVerificationStatus.Rejected && s.CompletedAtUtc >= since)
            .GroupBy(s => s.FailureReason)
            .Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var rejected = rejectedByReasonRows.Sum(r => r.Count);
        var rejectedByReason = rejectedByReasonRows
            .Where(r => r.Reason is not null)
            .ToDictionary(r => r.Reason!.Value.ToString(), r => r.Count);

        var dto = new PhoneVerificationDiagnosticsDto(
            enabled, options.Value.Provider, methods, diagnostics.WebhookSubscribed,
            diagnostics.LastSubscriptionAttemptAtUtc, diagnostics.LastSubscriptionError,
            new PhoneVerificationDailyCountsDto(started, verified, rejected, rejectedByReason));

        return Ok(dto);
    }
}
