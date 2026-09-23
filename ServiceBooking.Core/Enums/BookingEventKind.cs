namespace ServiceBooking.Core.Enums;

// ARCHITECTURE_CYCLE10.md §102.1: append-only — values are stored in the database, never renumbered.
public enum BookingEventKind
{
    Created = 0,
    Rescheduled = 1,
    Cancelled = 2,
    Completed = 3,
    NoShow = 4,
    PaymentMarked = 5,
}
