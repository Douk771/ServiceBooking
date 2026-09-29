using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>What a shop currently has switched on, as the pure planner needs it.</summary>
public sealed record OrderNotificationFlags(
    bool StaffPushEnabled, bool CustomerWebPushEnabled, bool CustomerMessengerEnabled, bool OrderNotifyByMessenger);

/// <summary>
/// The result of planning ONE journal event: which notification types go to whom. <c>StaffType</c> — push to the shop's staff;
/// <c>CustomerType</c> — the customer's channels, with the two channel switches already applied.
/// </summary>
public sealed record OrderNotificationPlan(
    NotificationType? StaffType, NotificationType? CustomerType, bool CustomerWebPush, bool CustomerMessenger)
{
    public static OrderNotificationPlan Nothing { get; } = new(null, null, false, false);

    public bool IsEmpty => StaffType is null && CustomerType is null;

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

        if (!flags.StaffPushEnabled) staff = null;
        var webPush = customer is not null && flags.CustomerWebPushEnabled;
        var messenger = customer is not null && flags.CustomerMessengerEnabled && flags.OrderNotifyByMessenger;
        if (!webPush && !messenger) customer = null;
        return new OrderNotificationPlan(staff, customer, webPush, messenger);
    }
}
