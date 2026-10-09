using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §457.3, API_CONTRACT_CYCLE24.md §483 (A10) — the notification settings of a SHOP, in one place: push to staff, web-push and
/// messenger to customers, the delivery mode and priority channel, and the numbers assigned to the shop. The salon controllers
/// (<c>CompanyNotificationsController</c>, <c>CompanyPushSettingsController</c>) stay closed for shops on purpose: their DTOs carry tariff 402s, templates
/// and a type mask — opening them would be a regression risk with no gain. This route writes the SAME rows (<c>CompanyNotificationSettings</c>,
/// <c>ShopSettings</c>). Connecting a number is the existing <c>api/notification-channels/*</c> (account level); this screen only shows the state.
/// GET — owner and staff; PUT — owner.
/// </summary>
[ApiController]
[Route("api/shops/{shopId:guid}/notification-settings")]
[Authorize]
public class ShopNotificationsController(
    AppDbContext db, ShopAccessResolver access, ShopChannelReader channelReader, IOptions<WebPushOptions> webPushOptions,
    PlatformSettings platformSettings, ServiceBooking.API.Services.StaffMax.StaffMaxAvailability staffMaxAvailability,
    AccountMessagingReader messagingReader) : ControllerBase
{
    public const string NoChannelText = "Подключите номер для сообщений покупателям";
    public const string NotFundedText = "Номер не оплачен — оставьте заявку на опцию в разделе «Подписка»";
    public const string MessengerUnavailableConflict = "Сначала подключите и оплатите номер для сообщений покупателям";
    public const string PriorityNotFunded = "Приоритетный канал должен быть среди оплаченных каналов магазина";

    [HttpGet]
    public async Task<ActionResult<ShopNotificationSettingsDto>> Get(Guid shopId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        return Ok(await BuildAsync(shopId, ct));
    }

    [HttpPut]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ShopNotificationSettingsDto>> Put(Guid shopId, ShopNotificationSettingsInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        if (input.DeliveryMode is { } mode && !Enum.IsDefined(mode) || input.PriorityTransport is { } transport && !Enum.IsDefined(transport))
            return BadRequest(PriorityNotFunded);

        // Cycle 40 (§40.6.4): "available" = the account has a PAID transport; the 409 for switching the flag on without one stays, the 400 for a priority
        // that is not funded is gone (the choice is a preference; routing falls back only by the mode's own rules).
        var messagingAtWrite = await messagingReader.ForCompanyAsync(shopId, ct: ct);
        if (input.CustomerMessengerEnabled && !messagingAtWrite.AnyPaid)
            return Conflict(new CatalogConflictDto(CatalogConflictCode.MessengerUnavailable, MessengerUnavailableConflict));

        var now = DateTime.UtcNow;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var shopSettings = await db.ShopSettings.FirstOrDefaultAsync(s => s.CompanyId == shopId, ct);
        if (shopSettings is null)
        {
            shopSettings = new ShopSettings { CompanyId = shopId };
            db.ShopSettings.Add(shopSettings);
        }
        shopSettings.CustomerWebPushEnabled = input.CustomerWebPushEnabled;
        shopSettings.CustomerMessengerEnabled = input.CustomerMessengerEnabled;
        // Cycle 25 (§524): the shop's MAX flag is independent of the platform switch and can be saved while the feature is off. Switching it off also
        // holds what is already queued (the dispatcher re-reads it at send time).
        if (input.StaffMaxEnabled is { } staffMaxEnabled) shopSettings.StaffMaxEnabled = staffMaxEnabled;
        shopSettings.UpdatedAtUtc = now;
        shopSettings.UpdatedByUserId = userId;

        var notificationSettings = await db.CompanyNotificationSettings.FirstOrDefaultAsync(s => s.CompanyId == shopId, ct);
        if (notificationSettings is null)
        {
            notificationSettings = new CompanyNotificationSettings { CompanyId = shopId };
            db.CompanyNotificationSettings.Add(notificationSettings);
        }
        notificationSettings.StaffPushEnabled = input.StaffPushEnabled;
        if (input.DeliveryMode is { } deliveryMode) notificationSettings.DeliveryMode = deliveryMode;
        if (input.PriorityTransport is { } priorityTransport) notificationSettings.PriorityTransport = priorityTransport;
        notificationSettings.UpdatedAt = now;
        notificationSettings.UpdatedByUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(await BuildAsync(shopId, ct));
    }

    private async Task<ShopNotificationSettingsDto> BuildAsync(Guid shopId, CancellationToken ct)
    {
        var shopSettings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shopId, ct) ?? new ShopSettings { CompanyId = shopId };
        var notificationSettings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shopId, ct)
                                   ?? new CompanyNotificationSettings { CompanyId = shopId };
        var channels = await channelReader.LoadAsync(shopId, ct);
        var idleDays = channels.Count == 0 ? 0 : await platformSettings.GetChannelIdleDaysAsync();

        var messaging = await messagingReader.ForCompanyAsync(shopId, ct: ct);
        var facts = messaging.Transports.Select(t => new TransportMessagingFacts(t.Transport, t.Option.Open, t.Paid, t.Routable, t.Working)).ToList();
        var status = CompanyMessagingStatus.Evaluate(messaging.PlatformEnabled, notificationSettings.DeliveryMode, notificationSettings.PriorityTransport, facts);
        var messengerAvailable = messaging.AnyPaid;
        var unavailableText = messengerAvailable ? null : CompanyMessagingStatus.MessengerUnavailableText(facts);
        var platformPush = string.Equals(webPushOptions.Value.Provider, "web-push", StringComparison.OrdinalIgnoreCase);

        var channelDtos = channels.Select(c =>
        {
            var masked = c.Channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(c.Channel.PhoneNumber);
            return new ShopChannelStatusDto(
                c.Channel.Id, c.Channel.Transport, masked,
                ChannelPresentation.StateText(c.Channel.State, masked, idleDays, c.Funding.PaidUntil, c.Channel.LastStateReason),
                c.Channel.State == ChannelState.Connected, c.IsFunded,
                c.IsFunded && c.Funding.PaidUntil is { } until ? $"Оплачен до {until:dd.MM.yyyy}" : c.Funding.Text);
        }).ToList();

        return new ShopNotificationSettingsDto(
            notificationSettings.StaffPushEnabled, shopSettings.CustomerWebPushEnabled, shopSettings.CustomerMessengerEnabled,
            notificationSettings.DeliveryMode, notificationSettings.PriorityTransport, messengerAvailable, unavailableText, platformPush, channelDtos,
            shopSettings.StaffMaxEnabled, staffMaxAvailability.Enabled,
            staffMaxAvailability.Enabled ? null : ServiceBooking.API.Services.StaffMax.StaffMaxAvailability.NotEnabledText,
            status.MessagingActive, status.DeliveryChoiceVisible, status.PriorityWarning);
    }
}
