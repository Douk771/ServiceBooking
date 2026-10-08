using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the superadmin trial routes of the former
/// <c>AdminBillingController</c> — same <c>api/admin</c> prefix, same SuperAdmin gate, same per-action routes.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminTrialController(
    AppDbContext db,
    SubscriptionResolver subscriptionResolver, AccountUsageReader usageReader,
    OwnerSubscriptionService ownerSubscriptionService, Services.Billing.TrialActivationService trialActivationService,
    Services.Notifications.AccountMessagingReader messagingReader) : ControllerBase
{
    // ── Cycle 18 (API_CONTRACT_CYCLE18.md §368) — superadmin trial grant/regrant ──────────────────

    [HttpPost("billing-accounts/{accountId:guid}/trial")]
    public async Task<IActionResult> GrantTrial(Guid accountId)
    {
        var account = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan).FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return NotFound();

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await trialActivationService.GrantAsync(new Services.Billing.TrialGrantRequest(
            accountId, actorUserId, Core.Enums.TrialGrantSource.SuperAdmin, Services.Billing.TrialGrantMode.Normal,
            Reason: null, AcknowledgedTermsVersion: null));

        if (!result.Granted)
            return Conflict(new DTOs.Billing.TrialRefusalDto(result.RefusalCode!, result.Message!));

        var fresh = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan).FirstAsync(a => a.Id == accountId);
        return Ok(await BuildAdminAccountDtoAsync(fresh));
    }

    [HttpPost("billing-accounts/{accountId:guid}/trial/regrant")]
    public async Task<IActionResult> RegrantTrial(Guid accountId, [FromBody] RegrantTrialInput dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length == 0)
            return BadRequest("Причина обязательна.");
        if (dto.Reason.Length > 500)
            return BadRequest("Причина не может быть длиннее 500 символов.");

        var account = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan).FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return NotFound();

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await trialActivationService.GrantAsync(new Services.Billing.TrialGrantRequest(
            accountId, actorUserId, Core.Enums.TrialGrantSource.SuperAdminOverride, Services.Billing.TrialGrantMode.SuperAdminOverride,
            Reason: dto.Reason, AcknowledgedTermsVersion: null));

        if (!result.Granted)
            return Conflict(new DTOs.Billing.TrialRefusalDto(result.RefusalCode!, result.Message!));

        var fresh = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan).FirstAsync(a => a.Id == accountId);
        return Ok(await BuildAdminAccountDtoAsync(fresh));
    }

    // Cycle 18 (API_CONTRACT_CYCLE18.md §376) — Т1: proving, months later, exactly which wording an
    // owner was shown at activation. Never removed from TrialTermsRegistry, so this always resolves for
    // any version that was ever CurrentVersion.
    [HttpGet("trial-terms/{version}")]
    public IActionResult GetTrialTerms(string version)
    {
        var template = Services.Billing.TrialTermsRegistry.TryGetTemplate(version);
        if (template is null) return NotFound();
        var sha256 = Services.Billing.TrialTermsRegistry.Sha256Of(version);
        var promisedThresholds = Services.Billing.TrialTermsRegistry.PromisedThresholdsByVersion.GetValueOrDefault(version, []);
        return Ok(new
        {
            version,
            sha256,
            isCurrent = version == Services.Billing.TrialTermsRegistry.CurrentVersion,
            promisedWarningThresholdsDays = promisedThresholds,
            template,
        });
    }

    // Cycle 22 P5 (§385): shared with the other half of the former AdminBillingController — the body lives
    // in AdminAccountDtoBuilder, unchanged.
    private Task<object> BuildAdminAccountDtoAsync(BillingAccount account) =>
        AdminAccountDtoBuilder.BuildAsync(db, subscriptionResolver, usageReader, ownerSubscriptionService, messagingReader, account);
}

public record RegrantTrialInput(string Reason);
