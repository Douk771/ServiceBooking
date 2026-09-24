using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Geo;

/// <summary>Computed, never persisted (ARCHITECTURE_CYCLE13.md §202/§203 — "Почему нет колонки
/// «статус»"). A second source of truth for "is this address verified" would drift from the address text
/// on any edit that goes through a path other than the one that maintains it explicitly.</summary>
public enum AddressVerificationStatus { Verified, Unverified }

/// <summary>
/// The one function that turns a <see cref="Company"/>'s address columns into a verification status
/// (ARCHITECTURE_CYCLE13.md §203, R5/R9/R18). Pure, no DB, no network — takes an already-loaded entity.
/// </summary>
public static class AddressVerificationState
{
    /// <summary>
    /// Verified iff the address is non-empty, a verification was recorded, AND the normalized key of the
    /// CURRENT address text still matches the key that was recorded at verification time. Any edit to
    /// <see cref="Company.Address"/> — through any of the four write paths (§202) — changes the key and
    /// silently drops back to Unverified; nothing needs to "reset" anything by hand.
    ///
    /// Unverified covers every other case, including: address never set, address cleared, a company
    /// created before this cycle (verification columns simply empty, US-137 — no backfill), and the
    /// address having been edited since the last verification.
    /// </summary>
    public static AddressVerificationStatus Status(Company company)
    {
        if (string.IsNullOrEmpty(company.Address)) return AddressVerificationStatus.Unverified;
        if (company.AddressVerifiedAt is null || company.AddressVerifiedInputKey is null)
            return AddressVerificationStatus.Unverified;

        return company.AddressVerifiedInputKey == AddressNormalization.Key(company.Address)
            ? AddressVerificationStatus.Verified
            : AddressVerificationStatus.Unverified;
    }
}
