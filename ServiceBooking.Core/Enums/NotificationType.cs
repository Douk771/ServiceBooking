namespace ServiceBooking.Core.Enums;

/// <summary>
/// What a queued/sent notification is about (ARCHITECTURE_CYCLE4.md §23.4). Also used as a bit position
/// in <c>CompanyNotificationSettings.EnabledTypeMask</c> (§23.3) — the underlying int values matter for
/// that bitmask and must stay append-only, same rule as <see cref="NotificationStatus"/>.
/// </summary>
public enum NotificationType
{
    BookingConfirmed,
    Reminder,
    BookingCancelled,
    BookingRescheduled,
    StaffBookingCreated,
    StaffBookingCancelled,
}
