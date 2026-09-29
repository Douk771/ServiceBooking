namespace ServiceBooking.Core.Enums;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2 — kind of an order journal entry. Persisted as a number: append-only.</summary>
public enum OrderEventKind
{
    Created = 0,
    Accepted = 1,
    Rejected = 2,
    MarkedReady = 3,
    Issued = 4,
    NotPickedUp = 5,
    CancelledByCustomer = 6,
    CancelledByShop = 7,
    Edited = 8
}
