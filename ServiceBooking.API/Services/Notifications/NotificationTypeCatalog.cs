using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §458 — which <see cref="NotificationType"/> members belong to bookings (the salon world) and which to orders.
/// The salon screens and the salon mask (<c>EnabledTypeMask</c>) speak ONLY about <see cref="BookingTypes"/>; whether order messages are
/// sent is decided by the shop's own flag, so a salon form save can never silently switch them off.
/// </summary>
public static class NotificationTypeCatalog
{
    public static readonly IReadOnlyList<NotificationType> BookingTypes =
    [
        NotificationType.BookingConfirmed, NotificationType.Reminder, NotificationType.BookingCancelled,
        NotificationType.BookingRescheduled, NotificationType.StaffBookingCreated, NotificationType.StaffBookingCancelled,
        NotificationType.StaffBookingRescheduled
    ];

    public static readonly IReadOnlyList<NotificationType> OrderTypes =
    [
        NotificationType.StaffOrderCreated, NotificationType.StaffOrderCancelledByCustomer, NotificationType.OrderAccepted,
        NotificationType.OrderReady, NotificationType.OrderRejected, NotificationType.OrderCancelledByShop,
        NotificationType.OrderEditedByShop, NotificationType.OrderPickupChanged, NotificationType.OwnerOrderLimitWarning
    ];

    public static bool IsBookingType(NotificationType type) => BookingTypes.Contains(type);

    public static bool IsOrderType(NotificationType type) => OrderTypes.Contains(type);

    /// <summary>The order notifications addressed to the CUSTOMER (web-push and messenger); the rest go to staff or the account owner.</summary>
    public static bool IsCustomerOrderType(NotificationType type) =>
        type is NotificationType.OrderAccepted or NotificationType.OrderReady or NotificationType.OrderRejected
            or NotificationType.OrderCancelledByShop or NotificationType.OrderEditedByShop or NotificationType.OrderPickupChanged;
}
