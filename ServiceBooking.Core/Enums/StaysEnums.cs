namespace ServiceBooking.Core.Enums;

// ARCHITECTURE_CYCLE37.md §37.2 — enums of the "Дома" (house rental) vertical. All are persisted as numbers:
// append-only, never reorder or renumber.

/// <summary>Position of a staff member (<c>CompanyMember.Role = Master</c>) in a "Дома" company (§37.9).</summary>
public enum StaffPosition
{
    Manager = 0,
    Housekeeper = 1
}

public enum StayBookingStatus
{
    Held = 0,
    AwaitingPaymentCheck = 1,
    Confirmed = 2,
    ExpiredUnpaid = 3,
    PaymentRejected = 4,
    CancelledByGuest = 5,
    CancelledByOwner = 6
}

public enum StayBookingEventKind
{
    Created = 0,
    PaymentProofUploaded = 1,
    PaymentProofViewed = 2,
    PaymentConfirmed = 3,
    HoldExpired = 4,
    PaymentRejected = 5,
    CancelledByGuest = 6,
    CancelledByOwner = 7,
    CheckInInfoReleased = 8,
    PaymentProofsPurged = 9,
    PersonalDataErased = 10,

    // ARCHITECTURE_CYCLE39.md §39.2.2 — sessions of services inside a booking and the arrival reminder.
    ServiceSessionAdded = 11,
    ServiceSessionCancelledByGuest = 12,
    ServiceSessionCancelledByOwner = 13,
    ServiceSessionsReleased = 14,
    ArrivalReminderSent = 15
}

public enum StayActorKind
{
    Guest = 0,
    Customer = 1,
    Staff = 2,
    SuperAdmin = 3,
    System = 4
}

/// <summary>ЮР-1: the three lawful cancellation templates (LEGAL_REVIEW_CYCLE37.md §6.2).</summary>
public enum StayCancellationPolicy
{
    Standard = 0,
    Flexible = 1,
    NoDeductions = 2
}

public enum StayChargeKind
{
    Nights = 0,
    ExtraBeds = 1,
    Dogs = 2,
    Cot = 3,
    ManualTotal = 4,

    // ARCHITECTURE_CYCLE39.md §39.2.2 — never PrepayEligible (CK_StayBookingCharges_ServiceNotPrepaid).
    ServiceSlot = 5,
    ServiceItem = 6
}

public enum StayProviderStatus
{
    Organization = 0,
    IndividualEntrepreneur = 1,
    SelfEmployed = 2,
    Individual = 3
}

public enum HousePriceMode
{
    Constant = 0,
    ByDates = 1
}

public enum HouseObjectKind
{
    GuestHouse = 0,
    OtherAccommodation = 1,
    Residential = 2
}

/// <summary>Bit positions of <c>House.AmenitiesMask</c> (§37.11.3): the numeric value is the bit index.</summary>
public enum HouseAmenity
{
    Wifi = 0,
    Kitchen = 1,
    Parking = 2,
    Sauna = 3,
    Bbq = 4,
    WashingMachine = 5,
    Tv = 6,
    Fireplace = 7,
    GearDryer = 8,
    SkiStorage = 9,
    LiftTransfer = 10,
    Dishwasher = 11,
    Terrace = 12
}

public enum HouseBlockKind
{
    Repair = 0,
    Personal = 1,
    Other = 2
}

public enum HouseBlockEventKind
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public enum OccupancySource
{
    PlatformBooking = 0,
    OwnerBlock = 1,
    /// <summary>Reserved for the iCal import of cycle 2 (§37.5.5).</summary>
    ExternalCalendar = 2
}

// ── ARCHITECTURE_CYCLE39.md §39.2.3–§39.2.4 — time-slot services (bath, hot tub, furaco). Persisted as numbers: append-only. ──

/// <summary>ЮР39-1: two lawful templates of a stand-alone session. A «Standard» template does not exist in any form.</summary>
public enum StayServiceCancellationPolicy
{
    NoDeductions = 0,
    PreparationCosts = 1
}

public enum StayServiceSessionState
{
    Active = 0,
    CancelledByGuest = 1,
    CancelledByOwner = 2,
    ReleasedWithBooking = 3,
    ReleasedWithOrder = 4
}

/// <summary>ЮР39-6: how the guest asked the staff to add a session (mandatory when the staff adds it).</summary>
public enum StayServiceRequestBasis
{
    Phone = 0,
    InPerson = 1,
    Messenger = 2
}

public enum StayServiceOrderEventKind
{
    Created = 0,
    PaymentProofUploaded = 1,
    PaymentProofViewed = 2,
    PaymentConfirmed = 3,
    HoldExpired = 4,
    PaymentRejected = 5,
    CancelledByGuest = 6,
    CancelledByOwner = 7,
    PaymentProofsPurged = 8,
    PersonalDataErased = 9
}

public enum StayServiceScheduleEventKind
{
    WeeklyTemplateChanged = 0,
    DateOverrideSet = 1,
    DateOverrideRemoved = 2
}
