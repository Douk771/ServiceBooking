namespace ServiceBooking.API.Services.Stays;

/// <summary>API_CONTRACT_CYCLE39.md §39.25 — why a choice of time is refused. The order of the first eleven is the order of <see cref="ServiceSlotCalculator.Diagnose"/>.</summary>
public enum ServiceRefusalCode
{
    ServiceOrdersDisabled, ServiceNotAvailableForStays, NotAcceptingBookings, DateInPast, BeyondHorizon, HoursOutOfRange, OutsideStay, TooEarly,
    StartUnavailable, NoPriceForHours, SlotTaken, ItemUnavailable, ItemQuantityExceeded, BookingNotActive, TooManySessions, PriceChanged
}

/// <summary>Why the list of starts of a date is empty.</summary>
public enum StartsReason { DateInPast, BeyondHorizon, Closed, NoStarts }

public enum ServiceRefundKind { NothingPaid, Full, PartialAtLeast, CostsOnlyUpTo }

/// <summary>Codes of the 409 of the services cabinet and of the reminder (<c>StaysServiceConflictDto</c>).</summary>
public enum StaysServiceConflictCode
{
    SlugInvalid, SlugTaken, ServiceHasSessions, ServiceArchived, ServiceNoPrice, ServiceNoWindows, ServiceLimitReached, PhotoLimitReached,
    ItemLimitReached, PriceRuleOverlap, PriceRuleLimitReached, ReminderConfirmationRequired
}
