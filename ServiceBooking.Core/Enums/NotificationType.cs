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

    // ARCHITECTURE_CYCLE15.md §251 p.4/§257.6 — appended at the end (append-only enum, bit position in
    // CompanyNotificationSettings.EnabledTypeMask). Queued only when a CLIENT reschedules their own
    // booking (StaffPushScheduler.OnBookingRescheduledAsync) — staff rescheduling never queues this,
    // same rule StaffBookingCreated already applies to the master's own action.
    StaffBookingRescheduled,

    // ARCHITECTURE_CYCLE24.md §458 — order notifications (bit positions 7–15 of the salon mask are never used by the
    // salon screens: NotificationTypeCatalog.BookingTypes filters them out of enabledTypes).
    StaffOrderCreated = 7,
    StaffOrderCancelledByCustomer = 8,
    OrderAccepted = 9,
    OrderReady = 10,
    OrderRejected = 11,
    OrderCancelledByShop = 12,
    OrderEditedByShop = 13,
    OrderPickupChanged = 14,
    OwnerOrderLimitWarning = 15,
}
