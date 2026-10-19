using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.3, API_CONTRACT_CYCLE43.md §43.22–§43.24 — the price screen of the two messenger options (WhatsApp, MAX):
/// the state with the journal, the consequences of a change without writing, and the change itself. SuperAdmin only.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminMessengerOptionsController(
    AppDbContext db, MessengerOptionsReader reader, PlatformSettings platformSettings, PricingCatalogCache pricingCatalogCache,
    IMemoryCache cache, ILogger<AdminMessengerOptionsController> logger) : ControllerBase
{
    private const string PriceRequired = "Укажите цену или null, чтобы снять цену";
    private const string ExpectedRequired = "Обновите страницу и повторите: не переданы цена и активность, которые вы видели на экране";

    [HttpGet("messenger-options")]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await reader.BuildAsync(ct));

    [HttpPost("messenger-options/{transport}/price/preview")]
    public async Task<IActionResult> Preview(string transport, [FromBody] MessengerPriceInput input, CancellationToken ct)
    {
        if (!MessengerTransportRoute.TryParse(transport, out var t)) return NotFound();
        if (!input.PricePerMonth.IsSpecified) return BadRequest(PriceRequired);
        var newPrice = input.PricePerMonth.Value;

        var option = await FindOptionAsync(t, tracked: false, ct);
        if (MessengerPriceChangeRules.Validate(newPrice, input.IsActive, Current(option), false, null) is { } error) return BadRequest(error);
        if (option is null) return Conflict(MessengerTexts.CatalogMissing(t));

        var before = new MessengerPriceState(option.PricePerMonth, option.IsActive);
        var after = new MessengerPriceState(newPrice, input.IsActive ?? option.IsActive);
        var activePaid = await reader.CountActivePaidAccountsAsync(t, option.Id, DateTime.UtcNow, ct);
        var consequences = MessengerPriceChangeRules.Consequences(
            t, before, after, await platformSettings.IsOptionOpenAsync(t, ct), reader.TermsOwnerPublished(option.Code), activePaid);

        return Ok(new MessengerPricePreviewDto(
            t, before.PricePerMonth, after.PricePerMonth, before.IsActive, after.IsActive, consequences.SellableBefore, consequences.SellableAfter,
            consequences.StartsSelling, consequences.StopsSelling, consequences.IsZeroPrice, activePaid, consequences.Texts,
            MessengerTexts.ConfirmTitle(t), MessengerTexts.ConfirmButtonText));
    }

    [HttpPut("messenger-options/{transport}/price")]
    public async Task<IActionResult> UpdatePrice(string transport, [FromBody] UpdateMessengerPriceInput input, CancellationToken ct)
    {
        if (!MessengerTransportRoute.TryParse(transport, out var t)) return NotFound();
        if (!input.PricePerMonth.IsSpecified) return BadRequest(PriceRequired);
        if (!input.ExpectedPricePerMonth.IsSpecified || input.ExpectedIsActive is null)
            return BadRequest(ExpectedRequired);
        var newPrice = input.PricePerMonth.Value;

        var snapshot = await FindOptionAsync(t, tracked: false, ct);
        if (MessengerPriceChangeRules.Validate(newPrice, input.IsActive, Current(snapshot), true, input.Comment) is { } error) return BadRequest(error);
        if (snapshot is null) return Conflict(MessengerTexts.CatalogMissing(t));
        var comment = input.Comment!.Trim();
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"subscription-option:{snapshot.Id}");

        // Under the lock the row is read again, from the database: the screen's numbers must be the ones the change is made over.
        var option = await db.SubscriptionOptions.FirstOrDefaultAsync(o => o.Id == snapshot.Id, ct);
        if (option is null) return Conflict(MessengerTexts.CatalogMissing(t));
        if (input.ExpectedPricePerMonth.Value != option.PricePerMonth || input.ExpectedIsActive.Value != option.IsActive)
            return Conflict(MessengerTexts.PriceChangedByOtherAdmin(t));
        if (MessengerPriceChangeRules.Validate(newPrice, input.IsActive, Current(option), true, comment) is { } lateError) return BadRequest(lateError);

        var (oldPrice, oldActive) = (option.PricePerMonth, option.IsActive);
        var now = DateTime.UtcNow;
        var activePaid = await reader.CountActivePaidAccountsAsync(t, option.Id, now, ct);
        option.PricePerMonth = newPrice;
        option.IsActive = input.IsActive ?? option.IsActive;
        option.UpdatedAtUtc = now;
        OptionPriceLog.Write(db, option, oldPrice, option.PricePerMonth, oldActive, option.IsActive, OptionPriceChangeSource.MessengerPriceScreen,
            activePaid, adminUserId, comment, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        MessengerCatalogCaches.Invalidate(pricingCatalogCache, cache);
        logger.LogInformation("Messenger {Transport} price changed by {UserId}: {Old} → {New}", t, adminUserId, oldPrice, option.PricePerMonth);
        return Ok(await reader.BuildOneAsync(t, ct));
    }

    private static MessengerPriceState? Current(SubscriptionOption? option) =>
        option is null ? null : new MessengerPriceState(option.PricePerMonth, option.IsActive);

    private async Task<SubscriptionOption?> FindOptionAsync(NotificationTransport t, bool tracked, CancellationToken ct)
    {
        var code = AccountMessagingReader.OptionCode(t);
        var query = tracked ? db.SubscriptionOptions : db.SubscriptionOptions.AsNoTracking();
        return await query.FirstOrDefaultAsync(o => o.Code == code, ct);
    }
}
