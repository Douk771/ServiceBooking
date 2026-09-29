namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §406.2 (US-20-05, Т20-06) — which of the four guest-data-gate check points
/// wrote a <see cref="Entities.GuestDataGateEvent"/> row. Append-only, stored as <c>int</c>.
/// </summary>
public enum GuestDataGateOperation
{
    Export = 0,
    DeleteAccount = 1,
    Revoke = 2,
    RevokePreview = 3,
}

/// <summary>The only outcome the journal ever records today — the gate applied and hid/blocked
/// something (ARCHITECTURE_CYCLE20.md §406.2). A single-member enum is deliberate: it keeps the column
/// self-describing in the database and ready for a future outcome without a data migration, rather than
/// hard-coding the string "Applied" in the writer.</summary>
public enum GuestDataGateOutcome
{
    Applied = 0,
}
