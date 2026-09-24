namespace ServiceBooking.Core.Enums;

/// <summary>
/// Which of the two entry points (ARCHITECTURE_CYCLE12.md §148, Q18) created a
/// <see cref="Core.Entities.PhoneVerificationSession"/>. The mechanism is identical for both — only
/// whether <c>UserId</c> is set and when the result is written (immediately vs. on registration) differs.
///
/// APPEND-ONLY: persisted on <see cref="Core.Entities.PhoneVerificationSession.Purpose"/>.
/// </summary>
public enum PhoneVerificationPurpose
{
    /// <summary>Anonymous caller, no account yet — the session is presented to <c>POST /api/auth/register</c>
    /// within its usable window (§142.1's <c>ConsumableUntilUtc</c>).</summary>
    Registration = 0,

    /// <summary>Authenticated caller confirming their own current (or about-to-change) number — the
    /// result is written the moment <c>contact</c> is accepted, no separate "consume" step (§148.3).</summary>
    Profile = 1,
}
