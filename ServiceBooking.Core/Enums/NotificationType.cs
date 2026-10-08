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

    // ARCHITECTURE_CYCLE37.md §37.12.1 — "Дома". Bit positions 16..26 are NOT used by the salon mask:
    // NotificationTypeCatalog.IsBookingType does not include them. Cycle 39 took 27..38.
    StaffStayCreated = 16,
    StaffStayPaymentProofUploaded = 17,
    StaffStayCancelledByGuest = 18,
    StayGuestCreated = 19,
    StayGuestHoldExpiring = 20,
    StayGuestHoldExpired = 21,
    StayGuestConfirmed = 22,
    StayGuestPaymentRejected = 23,
    StayGuestCancelledByOwner = 24,
    StayGuestArrivalReminder = 25,
    StayGuestCheckInInfo = 26,

    // ARCHITECTURE_CYCLE39.md §39.9.1 — time-slot services of «Дома». Like 16..26 they never use the salon mask (the mask is built from
    // NotificationTypeCatalog.BookingTypes only). 39 and 40 are reserved for cycle 40 (iCal): do not use them here.
    StaffStaySessionAdded = 27,
    StaffServiceOrderCreated = 28,
    StaffServiceOrderPaymentProofUploaded = 29,
    StaffServiceSessionCancelledByGuest = 30,
    ServiceGuestOrderCreated = 31,
    ServiceGuestHoldExpiring = 32,
    ServiceGuestHoldExpired = 33,
    ServiceGuestConfirmed = 34,
    ServiceGuestPaymentRejected = 35,
    ServiceGuestCancelledByOwner = 36,
    StayGuestSessionAdded = 37,
    StayGuestSessionCancelledByOwner = 38,
}
