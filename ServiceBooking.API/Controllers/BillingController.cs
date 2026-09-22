using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>contracts/openapi-cycle5.yaml tag billing-owner — the owner's own "Ваша подписка" screen
/// (GET /api/billing/subscription) and their plan/option request (US-65, US-68, US-69, US-70).</summary>
[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController(AppDbContext db, OwnerSubscriptionService ownerSubscriptionService) : ControllerBase
{
    [HttpGet("subscription")]
    public async Task<ActionResult<OwnerSubscriptionDto>> GetSubscription()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var dto = await ownerSubscriptionService.GetAsync(userId);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("subscription/request")]
    public async Task<IActionResult> SubmitRequest([FromBody] SubscriptionRequestInputDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var account = await ownerSubscriptionService.FindAccountForOwnerAsync(userId);
        if (account is null) return NotFound();

        if (dto.Comment is { Length: > 500 }) return BadRequest("Комментарий не может быть длиннее 500 символов.");

        var optionIds = dto.Options.Select(o => o.OptionId).ToList();
        var options = await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).ToListAsync();
        foreach (var line in dto.Options)
        {
            if (line.Quantity < 1) return BadRequest("Количество должно быть не меньше 1.");
            var option = options.FirstOrDefault(o => o.Id == line.OptionId);
            if (option is null) return BadRequest($"Опция {line.OptionId} не найдена.");
            if (option.Kind == OptionKind.Toggle && line.Quantity != 1)
                return BadRequest($"Опция «{option.Name}» — переключатель, количество может быть только 1.");
        }

        if (dto.PlanId.HasValue && !await db.SubscriptionPlanConfigs.AnyAsync(p => p.Id == dto.PlanId && p.IsActive))
            return BadRequest("Указанный тариф не найден или неактивен.");

        // Contract: repeated submission OVERWRITES the existing pending request and answers 200 — there
        // is no "already pending" 409 for the owner's own request, only for a superadmin racing an
        // approval against a resubmission (handled where the request is closed, not here).
        account.RequestedPlanId = dto.PlanId;
        account.RequestedOptionsJson = OwnerSubscriptionService.SerializeOptionLines(dto.Options.Select(o => new RequestedOptionLine(o.OptionId, o.Quantity)));
        account.RequestedAtUtc = DateTime.UtcNow;
        account.RequestedByUserId = userId;
        account.RequestedComment = dto.Comment;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var full = await ownerSubscriptionService.BuildAsync(account);
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
}
