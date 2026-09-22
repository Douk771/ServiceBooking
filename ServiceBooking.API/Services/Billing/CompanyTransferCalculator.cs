namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 7 (ARCHITECTURE_CYCLE7.md §51.1, §51.2) — the pure, DB-free rules behind
/// <see cref="CompanyTransferService"/>: whether a proposed new owner counts as "linked" to the
/// receiving account, and the seat-overflow formula that accounts for the +1 seat a not-yet-a-member
/// new owner would occupy (risk A8). Kept separate from the service so both can be unit-tested without
/// a database.
/// </summary>
public static class CompanyTransferCalculator
{
    /// <summary>§51.1 p.3 — a proposed new owner is "linked" to the receiving account if they're
    /// already its holder OR already a member of one of its companies. Either is enough; the two
    /// legitimate scenarios (selling to an outside buyer who holds their own account, or handing a
    /// branch to a manager already hired elsewhere in the same holding) each satisfy exactly one.</summary>
    public static bool IsNewOwnerLinkedToTargetAccount(bool isTargetAccountHolder, bool isMemberOfTargetAccountCompany) =>
        isTargetAccountHolder || isMemberOfTargetAccountCompany;

    /// <summary>
    /// §51.2's seatsAfter formula. <paramref name="newOwnerAddsSeat"/> must be true only when a new
    /// owner is actually being assigned AND that person is not already a member of the company being
    /// transferred (joining as owner would then occupy an additional seat on the receiving account).
    /// </summary>
    public static int ComputeSeatsAfter(int seatsUsedOnTargetAccount, int seatsOfTransferredCompany, bool newOwnerAddsSeat) =>
        seatsUsedOnTargetAccount + seatsOfTransferredCompany + (newOwnerAddsSeat ? 1 : 0);

    /// <summary>Whether §51.2's seat calculation overflows the target account's summed limit. `null`
    /// limit means unlimited.</summary>
    public static bool IsSeatOverflow(int seatsAfter, int? accountMaxEmployees) =>
        accountMaxEmployees is { } max && seatsAfter > max;

    /// <summary>§51.2's hard company-limit check. `null` limit means unlimited.</summary>
    public static bool IsCompanyLimitExceeded(int companiesUsedOnTargetAccount, int? accountMaxCompanies) =>
        accountMaxCompanies is { } max && companiesUsedOnTargetAccount >= max;
}
