using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>contracts/cycle7/openapi.yaml tag billing-owner — the owner's own "Ваша подписка" screen
/// (GET /api/billing/subscription) and their plan/option request (US-65, US-68, US-69, US-70).</summary>
[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController(
    AppDbContext db, OwnerSubscriptionService ownerSubscriptionService,
    TrialStateReader trialStateReader, TrialActivationService trialActivationService) : ControllerBase
{
    // ── Cycle 18 (API_CONTRACT_CYCLE18.md §362-§363.1) ────────────────────────────────────────────
    [HttpGet("trial")]
    public async Task<ActionResult<TrialStateDto>> GetTrial(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var dto = await trialStateReader.GetAsync(userId, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("trial")]
    [EnableRateLimiting("trial-activate")]
    public async Task<ActionResult<OwnerSubscriptionDto>> ActivateTrial([FromBody] TrialActivationRequestDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (string.IsNullOrWhiteSpace(dto.TermsVersion))
            return Conflict(new TrialRefusalDto("TrialTermsVersionMismatch", TrialLegalNotices.TrialTermsVersionMismatchNotice));

        // §363: "нет биллинг-аккаунта и он не создаётся (пользователь не владелец)" — 404. A
        // BillingAccount only exists once the user has provisioned one elsewhere (e.g. creating a
        // company); this endpoint must never be the thing that provisions one, or any authenticated
        // non-owner could burn their phone number in the once-only trial registry (Д16, 3-year retention,
        // not deletable) without ever becoming an owner.
        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, userId);
        if (accountId is null) return NotFound();

        var result = await trialActivationService.GrantAsync(new TrialGrantRequest(
            accountId.Value, userId, Core.Enums.TrialGrantSource.OwnerSelfService, TrialGrantMode.Normal, Reason: null,
            AcknowledgedTermsVersion: dto.TermsVersion));

        if (!result.Granted)
            return Conflict(new TrialRefusalDto(result.RefusalCode!, result.Message!));

        var subscriptionDto = await ownerSubscriptionService.GetAsync(userId);
        return Ok(subscriptionDto);
    }

    [HttpPost("trial/terms-acknowledgement")]
    public async Task<ActionResult<TrialStateDto>> AcknowledgeTrialTerms([FromBody] TrialActivationRequestDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await trialActivationService.AcknowledgeTermsAsync(userId, dto.TermsVersion);
        if (!result.Granted)
        {
            return result.RefusalCode switch
            {
                "TrialTermsVersionRequired" => BadRequest(result.Message),
                "TrialNotFound" => NotFound(),
                _ => Conflict(new TrialRefusalDto(result.RefusalCode!, result.Message!)),
            };
        }

        var state = await trialStateReader.GetAsync(userId);
        return state is null ? NotFound() : Ok(state);
    }

    [HttpGet("subscription")]
    public async Task<ActionResult<OwnerSubscriptionDto>> GetSubscription([FromQuery] string? line)
    {
        // ARCHITECTURE_CYCLE24.md §485.1: no `line` = "Записи", the answer of cycle 23 plus the three new fields at their defaults.
        if (!ServiceBooking.API.Services.Companies.CompanyKindQuery.TryParse(line, out var lineKind))
            return BadRequest(ServiceBooking.API.Services.Companies.CompanyKindQuery.UnknownKindText);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var dto = await ownerSubscriptionService.GetAsync(userId, lineKind);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("subscription/request")]
    public async Task<IActionResult> SubmitRequest([FromBody] SubscriptionRequestInputDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var account = await ownerSubscriptionService.FindAccountForOwnerAsync(userId);
        if (account is null) return NotFound();

        if (dto.Comment is { Length: > 500 }) return BadRequest("Комментарий не может быть длиннее 500 символов.");

        // ARCHITECTURE_CYCLE24.md §485.1: a request is for ONE line; the account holds ONE pending request in total.
        var requestLine = dto.Line ?? CompanyKind.Services;
        if (!Enum.IsDefined(requestLine)) return BadRequest(ServiceBooking.API.Services.Companies.CompanyKindQuery.UnknownKindText);

        // B6: an omitted `options` array is a request with no options, not a 500.
        var lines = dto.Options ?? [];

        var optionIds = lines.Select(o => o.OptionId).ToList();
        var options = await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).ToListAsync();
        foreach (var line in lines)
        {
            if (line.Quantity < 1) return BadRequest("Количество должно быть не меньше 1.");
            var option = options.FirstOrDefault(o => o.Id == line.OptionId);
            if (option is null) return BadRequest($"Опция {line.OptionId} не найдена.");
            // ARCHITECTURE_CYCLE19.md §408/§414 — a retired limit option can no longer be requested;
            // nothing is saved, the previous pending request (if any) stays as it was.
            if (RetiredLimitOptions.IsRetired(option))
                return BadRequest(BillingTexts.RetiredOptionRejected(option.Name));
            if (option.Kind == OptionKind.Toggle && line.Quantity != 1)
                return BadRequest($"Опция «{option.Name}» — переключатель, количество может быть только 1.");
        }

        SubscriptionPlanConfig? requestedPlan = null;
        if (dto.PlanId.HasValue)
        {
            requestedPlan = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.Id == dto.PlanId && p.IsActive);
            if (requestedPlan is null) return BadRequest("Указанный тариф не найден или неактивен.");
            if (requestedPlan.Line != requestLine) return BadRequest(BillingTexts.DifferentLine);
        }

        // Contract: repeated submission OVERWRITES the existing pending request and answers 200 — there
        // is no "already pending" 409 for the owner's own request, only for a superadmin racing an
        // approval against a resubmission (handled where the request is closed, not here).
        //
        // §52: lock on the billing account so this overwrite can't interleave with an admin approving/
        // rejecting the very request being overwritten (AdminBillingController uses the same key).
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{account.Id}");

        // A request of the OTHER line is waiting → 409 (checked under the account lock; the pending request itself is never overwritten across lines).
        if (account.RequestedAtUtc is not null && (account.RequestedLine ?? CompanyKind.Services) != requestLine)
            return Conflict(BillingTexts.RequestOfOtherLine(account.RequestedLine ?? CompanyKind.Services));

        account.RequestedLine = requestLine == CompanyKind.Services ? null : requestLine; // null = "Записи", exactly what every request before cycle 24 is
        account.RequestedPlanId = dto.PlanId;
        // N15 — keep the RequestedPlan navigation in sync with RequestedPlanId explicitly: `account`
        // was loaded with the OLD RequestedPlan already fixed up by the change tracker, and EF Core
        // does not re-resolve a nav property just because its FK scalar changed unless the new target
        // is also attached/tracked. BuildPendingRequestDto below reads account.RequestedPlan?.Name —
        // without this, a plan-change request would echo back the PREVIOUS pending plan's name (or
        // null on a first request) instead of the one just submitted.
        account.RequestedPlan = requestedPlan;
        account.RequestedOptionsJson = OwnerSubscriptionService.SerializeOptionLines(lines.Select(o => new RequestedOptionLine(o.OptionId, o.Quantity)));
        account.RequestedAtUtc = DateTime.UtcNow;
        account.RequestedByUserId = userId;
        account.RequestedComment = dto.Comment;
        // N10 — a fresh request supersedes any previous rejection reason; it stops being relevant the
        // moment the owner tries again.
        account.LastRejectionReason = null;
        account.LastRejectedAtUtc = null;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var full = await ownerSubscriptionService.BuildAsync(account, requestLine);
        return Ok(full.PendingRequest);
    }

    [HttpDelete("subscription/request")]
    public async Task<IActionResult> CancelRequest()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var account = await ownerSubscriptionService.FindAccountForOwnerAsync(userId);
        if (account is null) return NotFound();

        // Idempotent per contract — absence of a pending request is still 204, not an error.
        if (account.RequestedAtUtc is not null)
        {
            account.RequestedLine = null;
            account.RequestedPlanId = null;
            account.RequestedOptionsJson = null;
            account.RequestedAtUtc = null;
            account.RequestedByUserId = null;
            account.RequestedComment = null;
            account.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    // ARCHITECTURE_CYCLE20.md §402.5, API_CONTRACT_CYCLE20.md §432.8 (US-20-01, Т20-04 п. 3, D1 П-13) —
    // the PDn operator details printed on the health-consent paper form. Access is the billing account
    // HOLDER only (not a company manager) — same rule as every other /api/billing/* endpoint here.

    [HttpGet("operator-details")]
    public async Task<ActionResult<ConsentOperatorDetailsDto>> GetOperatorDetails()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var account = await ownerSubscriptionService.FindAccountForOwnerAsync(userId);
        if (account is null) return NotFound();

        return Ok(BuildOperatorDetailsDto(account));
    }

    [HttpPut("operator-details")]
    public async Task<ActionResult<ConsentOperatorDetailsDto>> PutOperatorDetails([FromBody] UpdateConsentOperatorDetailsDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var account = await ownerSubscriptionService.FindAccountForOwnerAsync(userId);
        if (account is null) return NotFound();

        var fullName = ConsentOperatorDetailsValidator.Normalize(dto.FullName);
        var address = ConsentOperatorDetailsValidator.Normalize(dto.Address);
        var inn = ConsentOperatorDetailsValidator.Normalize(dto.Inn);

        var error = ConsentOperatorDetailsValidator.Validate(fullName, address, inn);
        if (error is not null) return BadRequest(error);

        account.ConsentOperatorFullName = fullName;
        account.ConsentOperatorAddress = address;
        account.ConsentOperatorInn = inn;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(BuildOperatorDetailsDto(account));
    }

    private static ConsentOperatorDetailsDto BuildOperatorDetailsDto(BillingAccount account) => new(
        account.ConsentOperatorFullName, account.ConsentOperatorAddress, account.ConsentOperatorInn,
        ConsentOperatorDetailsValidator.IsMissing(account.ConsentOperatorFullName, account.ConsentOperatorAddress));
}

public record ConsentOperatorDetailsDto(string? FullName, string? Address, string? Inn, bool Missing);
public record UpdateConsentOperatorDetailsDto(string? FullName, string? Address, string? Inn);
