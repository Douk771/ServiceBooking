using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// Pure predicates for "is this presented/observed session usable here" (ARCHITECTURE_CYCLE14.md §148.1
/// step 3, §148.5, §148.3). Extracted out of <c>AuthController</c>/<c>ProfileController</c>/
/// <c>MaxWebhookHandler</c> per cycle-14 review finding 13 — these conditions are exactly where the
/// review's two blockers lived, and were previously inline `&amp;&amp;` chains with no unit coverage of
/// their own. No DB, no clock read (the caller passes <c>nowUtc</c> in) — just the five/four/one-fact
/// decision each endpoint actually makes.
/// </summary>
public static class PhoneVerificationSessionAcceptance
{
    /// <summary>§148.5's five conditions for a session presented to <c>POST /api/profile/change-phone</c>
    /// (US-14-17): it must be the caller's own, actually Verified (not merely Linked or already
    /// Consumed), a Profile-purpose session, still within its consumable window, and for the EXACT new
    /// number being requested — presenting a session verified for a different number proves nothing about
    /// this one.</summary>
    public static bool IsUsableForChangePhone(
        PhoneVerificationSession? candidate, bool tokenMatches, string userId, string newCanonicalPhone, DateTime nowUtc) =>
        candidate is not null && tokenMatches
        && candidate.Status == PhoneVerificationStatus.Verified
        && candidate.Purpose == PhoneVerificationPurpose.Profile
        && candidate.UserId == userId
        && candidate.CanonicalPhone == newCanonicalPhone  // SUBJECT-PHONE-GATE: not-account-scoped — cycle 14 phone-VERIFICATION subsystem itself deciding whether a session proves a number; not a TD-03 guest-data sewing point (ARCHITECTURE_CYCLE16.md §245.3)
        && candidate.ConsumableUntilUtc is { } consumableUntil && consumableUntil > nowUtc;

    /// <summary>API_CONTRACT_CYCLE14.md §168's equivalent gate for <c>POST /api/auth/register</c>: a
    /// Registration-purpose session, verified, not yet attached to any account, still within its
    /// consumable window, and for the exact phone the new account is registering with.</summary>
    public static bool IsUsableForRegistration(
        PhoneVerificationSession? candidate, bool tokenMatches, string canonicalPhone, DateTime nowUtc) =>
        candidate is not null && tokenMatches
        && candidate.Status == PhoneVerificationStatus.Verified
        && candidate.Purpose == PhoneVerificationPurpose.Registration
        && candidate.UserId == null
        && candidate.ConsumableUntilUtc is { } consumableUntil && consumableUntil > nowUtc
        && candidate.CanonicalPhone == canonicalPhone;  // SUBJECT-PHONE-GATE: not-account-scoped — cycle 14 phone-verification subsystem itself (ARCHITECTURE_CYCLE16.md §245.3)

    /// <summary>
    /// Cycle-12 review, blockers 1+2: a <c>Purpose=Profile</c> session is reused for two different
    /// scenarios that must NOT be handled alike once its <c>contact</c> is accepted —
    ///  - US-14-16, "confirm the number I already have": <paramref name="sessionCanonicalPhone"/> equals
    ///    the account's CURRENT <paramref name="accountPhoneNumber"/>. True here means: write
    ///    VerifiedPhone/PhoneNumberConfirmed and consume the session immediately — nobody will ever
    ///    "present" it anywhere else.
    ///  - US-14-17, the change-phone gate: the session's phone is a NEW number the account does not hold
    ///    yet. False here means: leave the session Verified (never write the mirror, never consume it) so
    ///    <c>ProfileController.ChangePhone</c> — the only caller allowed to consume it — can still find it
    ///    via <see cref="IsUsableForChangePhone"/> above.
    /// </summary>
    public static bool IsOwnCurrentNumberConfirmation(string sessionCanonicalPhone, string? accountPhoneNumber) =>
        accountPhoneNumber is not null && sessionCanonicalPhone == accountPhoneNumber;  // SUBJECT-PHONE-GATE: not-account-scoped — cycle 14 phone-verification subsystem itself (ARCHITECTURE_CYCLE16.md §245.3)
}
