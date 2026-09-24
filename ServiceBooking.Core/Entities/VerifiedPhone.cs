using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// Source of truth for "this phone number has been proven to belong to whoever controls it"
/// (ARCHITECTURE_CYCLE14.md §142.2, Q3, Q10). Verification belongs to the NUMBER (Р4), not to the
/// account that happened to hold it at the time — <see cref="Phone"/> is unique, and a number moving to
/// a different account (or a different MAX account) updates this row rather than creating a second one.
///
/// Deliberately no history table: the ceiling in §147.5 is a live <c>COUNT(*)</c> against this table's
/// CURRENT rows, and keeping only current state means deleting an account genuinely frees a slot (Q10)
/// instead of the count being permanently inflated by people who no longer have an account at all.
/// </summary>
public class VerifiedPhone
{
    public Guid Id { get; set; }

    /// <summary>Canonical phone. Unique — this table's whole point is "one row per verified number".</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Which method proved it — legal traceability ("what exactly verified this, a year later"),
    /// not a UI concern.</summary>
    public PhoneVerificationMethod Method { get; set; }

    /// <summary>HMAC-SHA256 of the MAX account that proved it (§142.3) — indexed, this is what the
    /// per-account ceiling (§147.5) counts against. Never exposed in any DTO.</summary>
    public string ExternalAccountKey { get; set; } = string.Empty;

    /// <summary>When this row last became true for the CURRENT (Phone, ExternalAccountKey) pair. Reusing
    /// the same account to re-verify the same number updates this timestamp in place rather than
    /// creating a duplicate row.</summary>
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Account the number belonged to at verification time. SetNull-independent by convention
    /// (the FK itself is Cascade, matching <see cref="PhoneVerificationSession.UserId"/>'s note — the
    /// cascade never actually fires because accounts are tombstoned, not deleted; <c>DeleteAccount</c>
    /// removes this row explicitly, §151.2).</summary>
    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Trace back to the session that produced this row, for incident review. SetNull — the
    /// session itself is retained only a day (§151.1) and outliving it is expected.</summary>
    public Guid? SessionId { get; set; }
    public PhoneVerificationSession? Session { get; set; }
}
