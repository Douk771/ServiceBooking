namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §572.1 — where a booking of a showcase (fictional) company came from. Stored as an integer;
/// append-only.
/// </summary>
public enum ShowcaseBookingKind
{
    /// <summary>An ordinary booking in an ordinary company.</summary>
    None = 0,

    /// <summary>Created by the showcase generator.</summary>
    Seeded = 1,

    /// <summary>Made by a site visitor in an open showcase company (D-1); purged by retention after a day.</summary>
    Visitor = 2,
}
