using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>What a shop currently has switched on, as the pure planner needs it.</summary>
public sealed record OrderNotificationFlags(
    bool StaffPushEnabled, bool CustomerWebPushEnabled, bool CustomerMessengerEnabled, bool OrderNotifyByMessenger,
    bool StaffMaxEnabled = false);

/// <summary>
/// The result of planning ONE journal event: which notification types go to whom. <c>StaffType</c> — push to the shop's staff;
/// <c>StaffMaxType</c> — the same event as a MAX message to the staff (ARCHITECTURE_CYCLE25.md §499.2): the same events and recipients as push,
/// but switched independently of it (so it is NOT derived from <c>StaffType</c>, which is null when push is off);
/// <c>CustomerType</c> — the customer's channels, with the two channel switches already applied.
/// </summary>
public sealed record OrderNotificationPlan(
    NotificationType? StaffType, NotificationType? CustomerType, bool CustomerWebPush, bool CustomerMessenger,
    NotificationType? StaffMaxType = null)
{
    public static OrderNotificationPlan Nothing { get; } = new(null, null, false, false);

    /// <summary>True when the event is announced to the staff in MAX.</summary>
    public bool StaffMax => StaffMaxType is not null;

    public bool IsEmpty => StaffType is null && CustomerType is null && StaffMaxType is null;

    /// <summary>
    /// ARCHITECTURE_CYCLE24.md §458 — the table "event → who is told what". <c>Issued</c>/<c>NotPickedUp</c> tell nobody, and the customer
    /// is never told about a cancellation they made themselves (US-24-22). A creation is announced to staff, and to the customer as
    /// "accepted" only when the shop accepts automatically.
    /// </summary>
    public static OrderNotificationPlan For(OrderEventKind kind, OrderStatus? toStatus, OrderNotificationFlags flags)
    {
        NotificationType? staff = kind switch
        {
            OrderEventKind.Created => NotificationType.StaffOrderCreated,
            OrderEventKind.CancelledByCustomer => NotificationType.StaffOrderCancelledByCustomer,
            _ => null
        };
        NotificationType? customer = kind switch
        {
            OrderEventKind.Created when toStatus == OrderStatus.Accepted => NotificationType.OrderAccepted,
            OrderEventKind.Accepted => NotificationType.OrderAccepted,
            OrderEventKind.MarkedReady => NotificationType.OrderReady,
            OrderEventKind.Rejected => NotificationType.OrderRejected,
            OrderEventKind.CancelledByShop => NotificationType.OrderCancelledByShop,
            OrderEventKind.Edited => NotificationType.OrderEditedByShop,
            OrderEventKind.PickupChanged => NotificationType.OrderPickupChanged,
            _ => null
        };

        var staffMax = flags.StaffMaxEnabled ? staff : null;
        if (!flags.StaffPushEnabled) staff = null;
        var webPush = customer is not null && flags.CustomerWebPushEnabled;
        var messenger = customer is not null && flags.CustomerMessengerEnabled && flags.OrderNotifyByMessenger;
        if (!webPush && !messenger) customer = null;
        return new OrderNotificationPlan(staff, customer, webPush, messenger, staffMax);
    }
}
