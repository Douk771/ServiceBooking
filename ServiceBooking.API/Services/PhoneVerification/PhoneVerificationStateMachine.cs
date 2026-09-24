using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// Pure state-transition rules for a <see cref="Core.Entities.PhoneVerificationSession"/>
/// (ARCHITECTURE_CYCLE12.md §145.2, §147). Takes only plain facts (current status, whether the TTL has
/// elapsed, the outcome of each check) and returns what should happen — no DB, no clock, no crypto. The
/// caller (<c>PhoneVerificationWebhookHandler</c>) is what actually reads/writes the entity and performs
/// the checks this class only interprets the RESULT of.
/// </summary>
public static class PhoneVerificationStateMachine
{
    /// <summary>§145.2: "Expired" is never a stored status — a session reads back as expired exactly
    /// when it is still Pending/Linked (never reached a terminal state on its own) and its TTL has
    /// elapsed. Shared by the poll endpoint (display) and the webhook handler (whether an incoming update
    /// should even be evaluated).</summary>
    public static bool IsExpired(PhoneVerificationStatus status, DateTime expiresAtUtc, DateTime nowUtc) =>
        status is PhoneVerificationStatus.Pending or PhoneVerificationStatus.Linked && expiresAtUtc < nowUtc;

    public enum StepOutcome
    {
        /// <summary>The event moves the session forward (Pending→Linked, Linked→Verified).</summary>
        Applied,

        /// <summary>Idempotent replay — nothing about the session's persisted state should change.</summary>
        NoOp,

        /// <summary>The event is rejected; for a <c>contact</c> this means the session moves to
        /// <see cref="PhoneVerificationStatus.Rejected"/> with the given reason. For a <c>bot_started</c>
        /// it means the session's status is left untouched (§145.2's "статус не меняется") — the reason
        /// is only used for the bot's own reply text.</summary>
        Rejected,
    }

    public readonly record struct BotStartedResult(StepOutcome Outcome, PhoneVerificationFailureReason? Reason);

    /// <summary>§145.2's <c>bot_started</c> row. A session already Verified/Rejected/Cancelled/Consumed
    /// rejects with <see cref="PhoneVerificationFailureReason.PayloadAlreadyUsed"/>; an expired one
    /// (Pending or Linked past its TTL) rejects with <see cref="PhoneVerificationFailureReason.PayloadExpired"/>;
    /// a first-time Pending session is accepted (→ Linked); a Linked session sees either a no-op (the
    /// SAME MAX account opening the link again) or <see cref="PhoneVerificationFailureReason.PayloadLinkedToAnotherAccount"/>
    /// (a different one).</summary>
    public static BotStartedResult EvaluateBotStarted(
        PhoneVerificationStatus status, bool expired, string? linkedExternalAccountKey, string incomingExternalAccountKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incomingExternalAccountKey);

        if (status is PhoneVerificationStatus.Rejected or PhoneVerificationStatus.Cancelled
            or PhoneVerificationStatus.Consumed or PhoneVerificationStatus.Verified)
            return new BotStartedResult(StepOutcome.Rejected, PhoneVerificationFailureReason.PayloadAlreadyUsed);

        if (expired)
            return new BotStartedResult(StepOutcome.Rejected, PhoneVerificationFailureReason.PayloadExpired);

        if (status == PhoneVerificationStatus.Pending)
            return new BotStartedResult(StepOutcome.Applied, null);

        // status == Linked
        return linkedExternalAccountKey == incomingExternalAccountKey
            ? new BotStartedResult(StepOutcome.NoOp, null)
            : new BotStartedResult(StepOutcome.Rejected, PhoneVerificationFailureReason.PayloadLinkedToAnotherAccount);
    }

    /// <summary>Every fact the <c>contact</c> event needs, already evaluated by the caller — this struct
    /// is what makes <see cref="EvaluateContact"/> itself free of crypto/parsing.</summary>
    public readonly record struct ContactChecks(
        bool SignatureValid, bool ContactOwnedBySender, bool HasUsablePhone, bool PhoneMatches, bool LimitReached);

    public readonly record struct ContactResult(StepOutcome Outcome, PhoneVerificationFailureReason? Reason);

    /// <summary>
    /// §145.2's <c>contact</c> row, §147's check ORDER (signature → ownership → vCard has a usable
    /// number → number matches the session → per-account ceiling — §145.1 step 3, §147.5's placement
    /// last "in the confirmation transaction"). A session that is already terminal (Verified/Rejected/
    /// Cancelled/Consumed) no-ops — this is what makes redelivery of the same webhook update safe
    /// (§145.2's "второй проход видит Verified/Rejected и не делает ничего"). A <c>contact</c> arriving
    /// while the session is still Pending (no <c>bot_started</c> ever recorded) is treated as
    /// <see cref="PhoneVerificationFailureReason.PayloadUnknown"/> — fail-closed, this should not happen
    /// through the bot's own conversation flow.
    /// </summary>
    public static ContactResult EvaluateContact(PhoneVerificationStatus status, bool expired, ContactChecks checks)
    {
        if (status is PhoneVerificationStatus.Verified or PhoneVerificationStatus.Rejected
            or PhoneVerificationStatus.Cancelled or PhoneVerificationStatus.Consumed)
            return new ContactResult(StepOutcome.NoOp, null);

        if (status == PhoneVerificationStatus.Pending)
            return new ContactResult(StepOutcome.Rejected, PhoneVerificationFailureReason.PayloadUnknown);

        if (expired)
            return new ContactResult(StepOutcome.Rejected, PhoneVerificationFailureReason.PayloadExpired);

        // status == Linked, from here on the §147 check order.
        if (!checks.SignatureValid)
            return new ContactResult(StepOutcome.Rejected, PhoneVerificationFailureReason.SignatureMismatch);
        if (!checks.ContactOwnedBySender)
            return new ContactResult(StepOutcome.Rejected, PhoneVerificationFailureReason.ContactNotOwnedBySender);
        if (!checks.HasUsablePhone)
            return new ContactResult(StepOutcome.Rejected, PhoneVerificationFailureReason.NoPhoneInContact);
        if (!checks.PhoneMatches)
            return new ContactResult(StepOutcome.Rejected, PhoneVerificationFailureReason.PhoneMismatch);
        if (checks.LimitReached)
            return new ContactResult(StepOutcome.Rejected, PhoneVerificationFailureReason.MaxAccountLimitReached);

        return new ContactResult(StepOutcome.Applied, null);
    }
}
