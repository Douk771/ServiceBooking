using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.6, API_CONTRACT_CYCLE43.md §43.25–§43.27 — a messenger of a billing account, separately from the tariff: the quote,
/// connect/extend and the switch-off. SuperAdmin only; the answer of the two writes is the account card.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminAccountMessengersController(
    AppDbContext db, MessengerPaymentService payments, SubscriptionResolver subscriptionResolver, AccountUsageReader usageReader,
    OwnerSubscriptionService ownerSubscriptionService, AccountMessagingReader messagingReader) : ControllerBase
{
    [HttpGet("billing-accounts/{accountId:guid}/messengers/{transport}/quote")]
    public async Task<IActionResult> Quote(Guid accountId, string transport, [FromQuery] int? months, CancellationToken ct)
    {
        if (!MessengerTransportRoute.TryParse(transport, out var t) || !await AccountExistsAsync(accountId, ct)) return NotFound();
        if (months is not { } m || m is < MessengerChargeRules.MinMonths or > MessengerChargeRules.MaxMonths) return BadRequest(MessengerTexts.MonthsRange);

        var result = await payments.QuoteAsync(accountId, t, m, ct);
        return result.Ok ? Ok(result.Value) : StatusCode(result.Status!.Value, result.Text);
    }

    [HttpPost("billing-accounts/{accountId:guid}/messengers/{transport}/extend")]
    public async Task<IActionResult> Extend(Guid accountId, string transport, [FromBody] ExtendMessengerInput input, CancellationToken ct)
    {
        if (!MessengerTransportRoute.TryParse(transport, out var t) || !await AccountExistsAsync(accountId, ct)) return NotFound();
        if (input.Months is < MessengerChargeRules.MinMonths or > MessengerChargeRules.MaxMonths) return BadRequest(MessengerTexts.MonthsRange);
        if (input.Comment is { Length: > 500 }) return BadRequest(MessengerTexts.ExtendCommentTooLong);
        if (input.ExpectedPricePerMonth is null) return BadRequest(MessengerTexts.ExpectedPriceMissing);

        var result = await payments.ExtendAsync(
            accountId, t, input.Months, input.Comment?.Trim(), input.ExpectedPricePerMonth, User.FindFirstValue(ClaimTypes.NameIdentifier)!, null, ct);
        return result.Ok ? Ok(await BuildCardAsync(accountId, ct)) : StatusCode(result.Status!.Value, result.Text);
    }

    [HttpPost("billing-accounts/{accountId:guid}/messengers/{transport}/disable")]
    public async Task<IActionResult> Disable(Guid accountId, string transport, [FromBody] DisableMessengerInput input, CancellationToken ct)
    {
        if (!MessengerTransportRoute.TryParse(transport, out var t) || !await AccountExistsAsync(accountId, ct)) return NotFound();
        if (input.When is not { } when) return BadRequest(MessengerTexts.DisableWhenRequired);
        var comment = input.Comment?.Trim();
        if (string.IsNullOrEmpty(comment)) return BadRequest(MessengerTexts.DisableCommentRequired);
        if (comment.Length > 500) return BadRequest(MessengerTexts.DisableCommentTooLong);

        var result = await payments.DisableAsync(accountId, t, when, comment, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        return result.Ok ? Ok(await BuildCardAsync(accountId, ct)) : StatusCode(result.Status!.Value, result.Text);
    }

    private Task<bool> AccountExistsAsync(Guid accountId, CancellationToken ct) => db.BillingAccounts.AsNoTracking().AnyAsync(a => a.Id == accountId, ct);

    private async Task<object> BuildCardAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan).FirstAsync(a => a.Id == accountId, ct);
        return await AdminAccountDtoBuilder.BuildAsync(db, subscriptionResolver, usageReader, ownerSubscriptionService, messagingReader, account);
    }
}
