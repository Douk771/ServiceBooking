using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public enum StayScheduledKind { HoldExpiring, ArrivalReminder }

public enum StayAudience { Staff, Guest }

public readonly record struct PlannedNotification(NotificationType Type, StayAudience Audience);

/// <summary>ARCHITECTURE_CYCLE37.md §37.12.1 — the table "event → who gets what". Pure.</summary>
public static class StayNotificationPlan
{
    public static IReadOnlyList<PlannedNotification> ForEvent(StayBookingEventKind kind, bool manualBooking = false, bool sessionByStaff = false, bool viaBooking = false) => kind switch
    {
        StayBookingEventKind.ServiceSessionAdded when viaBooking => [],
        StayBookingEventKind.ServiceSessionAdded when sessionByStaff && manualBooking => [],
        StayBookingEventKind.ServiceSessionAdded when sessionByStaff => [new(NotificationType.StayGuestSessionAdded, StayAudience.Guest)],
        StayBookingEventKind.ServiceSessionAdded => [new(NotificationType.StaffStaySessionAdded, StayAudience.Staff)],
        StayBookingEventKind.ServiceSessionCancelledByGuest => [new(NotificationType.StaffServiceSessionCancelledByGuest, StayAudience.Staff)],
        StayBookingEventKind.ServiceSessionCancelledByOwner => [new(NotificationType.StayGuestSessionCancelledByOwner, StayAudience.Guest)],
        StayBookingEventKind.Created when manualBooking => [],
        StayBookingEventKind.Created => [new(NotificationType.StaffStayCreated, StayAudience.Staff), new(NotificationType.StayGuestCreated, StayAudience.Guest)],
        StayBookingEventKind.PaymentProofUploaded => [new(NotificationType.StaffStayPaymentProofUploaded, StayAudience.Staff)],
        StayBookingEventKind.CancelledByGuest => [new(NotificationType.StaffStayCancelledByGuest, StayAudience.Staff)],
        StayBookingEventKind.HoldExpired => [new(NotificationType.StayGuestHoldExpired, StayAudience.Guest)],
        StayBookingEventKind.PaymentConfirmed => [new(NotificationType.StayGuestConfirmed, StayAudience.Guest)],
        StayBookingEventKind.PaymentRejected => [new(NotificationType.StayGuestPaymentRejected, StayAudience.Guest)],
        StayBookingEventKind.CancelledByOwner => [new(NotificationType.StayGuestCancelledByOwner, StayAudience.Guest)],
        StayBookingEventKind.CheckInInfoReleased => [new(NotificationType.StayGuestCheckInInfo, StayAudience.Guest)],
        _ => []
    };

    public static PlannedNotification ForScheduled(StayScheduledKind kind) => kind switch
    {
        StayScheduledKind.HoldExpiring => new(NotificationType.StayGuestHoldExpiring, StayAudience.Guest),
        _ => new(NotificationType.StayGuestArrivalReminder, StayAudience.Guest)
    };

    /// <summary>ARCHITECTURE_CYCLE39.md §39.9.2 — the table «event of an order → who gets what».</summary>
    public static IReadOnlyList<PlannedNotification> ForOrderEvent(StayServiceOrderEventKind kind, bool manualOrder = false) => kind switch
    {
        StayServiceOrderEventKind.Created when manualOrder => [],
        StayServiceOrderEventKind.Created => [new(NotificationType.StaffServiceOrderCreated, StayAudience.Staff), new(NotificationType.ServiceGuestOrderCreated, StayAudience.Guest)],
        StayServiceOrderEventKind.PaymentProofUploaded => [new(NotificationType.StaffServiceOrderPaymentProofUploaded, StayAudience.Staff)],
        StayServiceOrderEventKind.CancelledByGuest => [new(NotificationType.StaffServiceSessionCancelledByGuest, StayAudience.Staff)],
        StayServiceOrderEventKind.HoldExpired => [new(NotificationType.ServiceGuestHoldExpired, StayAudience.Guest)],
        StayServiceOrderEventKind.PaymentConfirmed => [new(NotificationType.ServiceGuestConfirmed, StayAudience.Guest)],
        StayServiceOrderEventKind.PaymentRejected => [new(NotificationType.ServiceGuestPaymentRejected, StayAudience.Guest)],
        StayServiceOrderEventKind.CancelledByOwner => [new(NotificationType.ServiceGuestCancelledByOwner, StayAudience.Guest)],
        StayServiceOrderEventKind.SessionReminderSent => [new(NotificationType.ServiceGuestSessionReminder, StayAudience.Guest)],
        _ => []
    };
}
