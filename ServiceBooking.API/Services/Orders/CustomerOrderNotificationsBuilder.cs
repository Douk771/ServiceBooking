using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §456.1, API_CONTRACT_CYCLE24.md §479 — what the order page may offer about notifications in THIS browser.
/// <c>webPush.available</c> is false in three cases, each with its own <c>unavailableText</c>: the shop switched web-push off (no text — the button
/// is simply not shown), the platform has push off (text), the order is in a final status (no text). The VAPID public key is handed out here
/// and nowhere else anonymously.
/// </summary>
public sealed class CustomerOrderNotificationsBuilder(IOptions<WebPushOptions> webPushOptions)
{
    public const string PlatformOffText = "Уведомления в браузере пока не включены на платформе";

    public bool PlatformPushEnabled => string.Equals(webPushOptions.Value.Provider, "web-push", StringComparison.OrdinalIgnoreCase);

    public OrderCustomerNotificationsDto Build(Order order, ShopSettings settings)
    {
        OrderWebPushInfoDto webPush;
        if (!settings.CustomerWebPushEnabled) webPush = new OrderWebPushInfoDto(false, null, null);
        else if (!PlatformPushEnabled) webPush = new OrderWebPushInfoDto(false, null, PlatformOffText);
        else if (OrderStateMachine.IsTerminal(order.Status)) webPush = new OrderWebPushInfoDto(false, null, null);
        else webPush = new OrderWebPushInfoDto(true, webPushOptions.Value.VapidPublicKey, null);
        return new OrderCustomerNotificationsDto(webPush, order.NotifyByMessenger);
    }
}
