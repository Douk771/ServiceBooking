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

    /// <summary>ARCHITECTURE_CYCLE37.md §37.12.1 — the "Дома" types (16..26). They never use the salon bitmask.</summary>
    public static readonly IReadOnlyList<NotificationType> StayTypes =
    [
        NotificationType.StaffStayCreated, NotificationType.StaffStayPaymentProofUploaded, NotificationType.StaffStayCancelledByGuest,
        NotificationType.StayGuestCreated, NotificationType.StayGuestHoldExpiring, NotificationType.StayGuestHoldExpired,
        NotificationType.StayGuestConfirmed, NotificationType.StayGuestPaymentRejected, NotificationType.StayGuestCancelledByOwner,
        NotificationType.StayGuestArrivalReminder, NotificationType.StayGuestCheckInInfo
    ];

    /// <summary>
    /// ARCHITECTURE_CYCLE39.md §39.9.1 — the time-slot services of «Дома» (27..38). Like <see cref="StayTypes"/> they have no bit in the salon mask: the mask
    /// is built and read from <see cref="BookingTypes"/> only (a shift by ≥ 32 would wrap around in C# and flip a salon bit).
    /// </summary>
    public static readonly IReadOnlyList<NotificationType> ServiceTypes =
    [
        NotificationType.StaffStaySessionAdded, NotificationType.StaffServiceOrderCreated, NotificationType.StaffServiceOrderPaymentProofUploaded,
        NotificationType.StaffServiceSessionCancelledByGuest, NotificationType.ServiceGuestOrderCreated, NotificationType.ServiceGuestHoldExpiring,
        NotificationType.ServiceGuestHoldExpired, NotificationType.ServiceGuestConfirmed, NotificationType.ServiceGuestPaymentRejected,
        NotificationType.ServiceGuestCancelledByOwner, NotificationType.StayGuestSessionAdded, NotificationType.StayGuestSessionCancelledByOwner,
        NotificationType.ServiceGuestSessionReminder
    ];

    public static bool IsServiceType(NotificationType type) => ServiceTypes.Contains(type);

    public static bool IsStayType(NotificationType type) => StayTypes.Contains(type) || ServiceTypes.Contains(type);

    public static bool IsBookingType(NotificationType type) => BookingTypes.Contains(type);

    public static bool IsOrderType(NotificationType type) => OrderTypes.Contains(type);

    /// <summary>The order notifications addressed to the CUSTOMER (web-push and messenger); the rest go to staff or the account owner.</summary>
    public static bool IsCustomerOrderType(NotificationType type) =>
        type is NotificationType.OrderAccepted or NotificationType.OrderReady or NotificationType.OrderRejected
            or NotificationType.OrderCancelledByShop or NotificationType.OrderEditedByShop or NotificationType.OrderPickupChanged;
}
