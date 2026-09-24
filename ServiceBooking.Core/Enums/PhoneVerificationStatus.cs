namespace ServiceBooking.Core.Enums;

/// <summary>
/// State machine of a <see cref="Core.Entities.PhoneVerificationSession"/> (ARCHITECTURE_CYCLE12.md
/// §145.2):
/// <code>
/// Pending ──bot_started(ok)──► Linked ──contact(all checks pass)──► Verified ──register/profile──► Consumed
///    │                            │                                    │
///    │                            └──contact(any check fails)──────────┴──► Rejected(FailureReason)
///    ├──TTL elapsed──► (computed as Expired, never stored)
///    └──DELETE /sessions/{id}──► Cancelled
/// </code>
/// <c>Expired</c> is NEVER written to the database — it is computed by the reader
/// (<c>Status is Pending or Linked</c> and <c>ExpiresAtUtc &lt; now</c>), exactly as the API contract
/// (§164) documents. Transitions themselves live in <c>PhoneVerificationStateMachine</c> — a pure
/// function this enum's values are the vocabulary for.
///
/// APPEND-ONLY: persisted on <see cref="Core.Entities.PhoneVerificationSession.Status"/>.
/// </summary>
public enum PhoneVerificationStatus
{
    Pending = 0,
    Linked = 1,
    Verified = 2,
    Rejected = 3,
    Cancelled = 4,
    Consumed = 5,
}
