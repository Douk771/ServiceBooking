namespace ServiceBooking.API.Services.Subjects;

/// <summary>
/// TD-03 (ARCHITECTURE_CYCLE16.md §245.4). The scope of "this subject's own data" for one
/// account-scoped request. Every predicate in the codebase that currently reads
/// <c>x.ClientId == userId || (canonicalPhone != null &amp;&amp; x.GuestPhone matches canonicalPhone)</c>  // SUBJECT-PHONE-GATE: not-account-scoped — doc comment, not executable code
/// keeps that exact shape — only the local variable it uses changes, from a single ambiguous
/// <c>canonicalPhone</c> to one of the two named fields below (see §245.4's distribution table for
/// which field each of the eleven call sites needs).
/// </summary>
/// <param name="UserId">The account this scope was resolved for.</param>
/// <param name="OwnPhone">
/// The account's own contact number (<c>AppUser.PhoneNumber</c>), regardless of verification. Always
/// safe to show back to the account itself (e.g. <c>ExportProfileDto.Phone</c>) — it is the account's
/// own data, not a guest-matched string.
/// </param>
/// <param name="GuestMatchPhone">
/// The phone string to use for "find rows recorded only under this phone number" queries — but ONLY
/// when this account has proven it owns that number (a row exists in <c>VerifiedPhones</c> for this
/// exact UserId/Phone pair). <c>null</c> means: the phone-matching branch must not run at all for this
/// request — every predicate using it degrades to its ClientId/UserId/RecipientUserId half only, which
/// is exactly what "gate closed" means (the predicates were always written to tolerate a null phone).
/// </param>
public readonly record struct SubjectScope(string UserId, string? OwnPhone, string? GuestMatchPhone)
{
    /// <summary>
    /// True when this account has a phone number but has not proven it owns it, i.e. the
    /// phone-matching branch was suppressed for this request. Used only for the user-facing signal
    /// (§245.6's <c>guestDataGate.applied</c>) and the Information-level log line (§245.7) — never for
    /// deciding what to query, which is <see cref="GuestMatchPhone"/>'s job alone.
    /// </summary>
    public bool GateApplied => OwnPhone is not null && GuestMatchPhone is null;
}
