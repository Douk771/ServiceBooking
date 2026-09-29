namespace ServiceBooking.Core.Enums;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2, §396.1 — persisted as a number: append-only (SQL filters use 0,1,2).</summary>
public enum OrderStatus
{
    New = 0,
    Accepted = 1,
    Ready = 2,
    Issued = 3,
    Rejected = 4,
    CancelledByCustomer = 5,
    CancelledByShop = 6,
    NotPickedUp = 7
}
