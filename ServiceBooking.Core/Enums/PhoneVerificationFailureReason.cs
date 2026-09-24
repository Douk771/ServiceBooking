namespace ServiceBooking.Core.Enums;

/// <summary>
/// Machine-readable reason a <see cref="Core.Entities.PhoneVerificationSession"/> ended up
/// <see cref="PhoneVerificationStatus.Rejected"/> (ARCHITECTURE_CYCLE14.md §147.4). The Russian sentence
/// shown to a person is composed server-side (<c>PhoneVerificationTexts</c>) from this code — the
/// frontend never invents wording, it only maps a code it doesn't recognise to a generic fallback.
///
/// APPEND-ONLY: persisted on <see cref="Core.Entities.PhoneVerificationSession.FailureReason"/> and
/// exposed verbatim in <c>GET /phone-verification/sessions/{id}</c> (API_CONTRACT_CYCLE14.md §165).
/// </summary>
public enum PhoneVerificationFailureReason
{
    /// <summary>No session matches the payload hash from <c>bot_started</c> — an unknown/mistyped/already
    /// garbage-collected link.</summary>
    PayloadUnknown = 0,

    /// <summary>The session existed but its TTL had already elapsed when the update arrived.</summary>
    PayloadExpired = 1,

    /// <summary>The session was already in a terminal state (Verified/Rejected/Cancelled/Consumed) when
    /// a NEW payload/contact update arrived for it.</summary>
    PayloadAlreadyUsed = 2,

    /// <summary>§145.2: a second <c>bot_started</c> for the same payload arrived from a DIFFERENT MAX
    /// account than the one that first linked it.</summary>
    PayloadLinkedToAnotherAccount = 3,

    /// <summary>§147.1: <c>HMAC-SHA256(botToken, vcf_info)</c> did not match the platform-supplied
    /// <c>hash</c> — the contact attachment cannot be trusted to be what the bot actually sent.</summary>
    SignatureMismatch = 4,

    /// <summary>§147.2 — the главный сценарий безопасности цикла (US-14-04): the contact's own owner id
    /// (<c>max_info</c>/<c>tam_info</c>) does not equal the update's sender id. Covers both "different
    /// person" and "unknown owner" (fail-closed, §147.2).</summary>
    ContactNotOwnedBySender = 5,

    /// <summary>§147.3: the vCard parsed cleanly but contained no <c>TEL</c> value that canonicalizes.</summary>
    NoPhoneInContact = 6,

    /// <summary>§147.3: at least one usable phone was found, but none of them equals the session's
    /// <c>CanonicalPhone</c>.</summary>
    PhoneMismatch = 7,

    /// <summary>§147.5 — the same MAX account already has <c>PhoneVerification:MaxPhonesPerExternalAccount</c>
    /// (3) OTHER numbers verified.</summary>
    MaxAccountLimitReached = 8,

    /// <summary>The person (or the form, on a phone edit — §145.3) cancelled the session before it
    /// reached a terminal outcome on its own.</summary>
    SessionCancelled = 9,

    /// <summary>The subsystem was switched off mid-scenario — the session cannot be completed even
    /// though it was created while the subsystem was still enabled.</summary>
    SubsystemDisabled = 10,
}
