using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public enum StayScheduledKind { HoldExpiring, ArrivalReminder }

public enum StayAudience { Staff, Guest }

public readonly record struct PlannedNotification(NotificationType Type, StayAudience Audience);

/// <summary>ARCHITECTURE_CYCLE37.md §37.12.1 — the table "event → who gets what". Pure.</summary>
public static class StayNotificationPlan
{
    public static IReadOnlyList<PlannedNotification> ForEvent(StayBookingEventKind kind, bool manualBooking = false) => kind switch
    {
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

    public static bool IsStayType(NotificationType t) => (int)t >= 16 && (int)t <= 26;
}
