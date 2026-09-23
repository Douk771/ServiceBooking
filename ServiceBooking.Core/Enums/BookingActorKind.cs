namespace ServiceBooking.Core.Enums;

// ARCHITECTURE_CYCLE10.md §102.1: append-only — values are stored in the database, never renumbered.
// `System` is not emitted by anything in cycle 10 (US-123) — it exists because the entity must be able
// to represent a system-originated event, and adding an enum member later is more expensive than now.
public enum BookingActorKind
{
    Client = 0,
    Guest = 1,
    Staff = 2,
    SuperAdmin = 3,
    System = 4,
}
