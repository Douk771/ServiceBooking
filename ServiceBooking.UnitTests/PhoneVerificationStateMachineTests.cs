using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Enums;
using static ServiceBooking.API.Services.PhoneVerification.PhoneVerificationStateMachine;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE12.md §145.2, §147, §156.1 — transitions and failure reasons, as a pure
/// function of already-evaluated facts.</summary>
public class PhoneVerificationStateMachineTests
{
    // ── IsExpired ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PhoneVerificationStatus.Pending, true)]
    [InlineData(PhoneVerificationStatus.Linked, true)]
    [InlineData(PhoneVerificationStatus.Verified, false)]
    [InlineData(PhoneVerificationStatus.Rejected, false)]
    [InlineData(PhoneVerificationStatus.Cancelled, false)]
    [InlineData(PhoneVerificationStatus.Consumed, false)]
    public void IsExpired_OnlyPendingOrLinkedPastTtlCountsAsExpired(PhoneVerificationStatus status, bool expected)
    {
        var now = DateTime.UtcNow;
        IsExpired(status, expiresAtUtc: now.AddMinutes(-1), now).Should().Be(expected);
    }

    [Fact]
    public void IsExpired_NotYetPastTtl_ReturnsFalse() =>
        IsExpired(PhoneVerificationStatus.Pending, DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow).Should().BeFalse();

    // ── EvaluateBotStarted ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BotStarted_FirstTimeOnPendingSession_Applies()
    {
        var result = EvaluateBotStarted(PhoneVerificationStatus.Pending, expired: false, linkedExternalAccountKey: null, incomingExternalAccountKey: "acct-a");
        result.Outcome.Should().Be(StepOutcome.Applied);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void BotStarted_SameAccountReopensLinkedSession_IsNoOp()
    {
        var result = EvaluateBotStarted(PhoneVerificationStatus.Linked, expired: false, linkedExternalAccountKey: "acct-a", incomingExternalAccountKey: "acct-a");
        result.Outcome.Should().Be(StepOutcome.NoOp);
    }

    [Fact]
    public void BotStarted_DifferentAccountOnLinkedSession_IsRejectedWithLinkedToAnotherAccount()
    {
        var result = EvaluateBotStarted(PhoneVerificationStatus.Linked, expired: false, linkedExternalAccountKey: "acct-a", incomingExternalAccountKey: "acct-b");
        result.Outcome.Should().Be(StepOutcome.Rejected);
        result.Reason.Should().Be(PhoneVerificationFailureReason.PayloadLinkedToAnotherAccount);
    }

    [Theory]
    [InlineData(PhoneVerificationStatus.Pending)]
    [InlineData(PhoneVerificationStatus.Linked)]
    public void BotStarted_ExpiredSession_IsRejectedWithPayloadExpired(PhoneVerificationStatus status)
    {
        var result = EvaluateBotStarted(status, expired: true, linkedExternalAccountKey: null, incomingExternalAccountKey: "acct-a");
        result.Outcome.Should().Be(StepOutcome.Rejected);
        result.Reason.Should().Be(PhoneVerificationFailureReason.PayloadExpired);
    }

    [Theory]
    [InlineData(PhoneVerificationStatus.Verified)]
    [InlineData(PhoneVerificationStatus.Rejected)]
    [InlineData(PhoneVerificationStatus.Cancelled)]
    [InlineData(PhoneVerificationStatus.Consumed)]
    public void BotStarted_TerminalSession_IsRejectedWithPayloadAlreadyUsed(PhoneVerificationStatus status)
    {
        var result = EvaluateBotStarted(status, expired: false, linkedExternalAccountKey: "acct-a", incomingExternalAccountKey: "acct-a");
        result.Outcome.Should().Be(StepOutcome.Rejected);
        result.Reason.Should().Be(PhoneVerificationFailureReason.PayloadAlreadyUsed);
    }

    // ── EvaluateContact ─────────────────────────────────────────────────────────────────────────────

    private static readonly ContactChecks AllPass = new(
        SignatureValid: true, ContactOwnedBySender: true, HasUsablePhone: true, PhoneMatches: true, LimitReached: false);

    [Fact]
    public void Contact_AllChecksPass_Applies()
    {
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: false, AllPass);
        result.Outcome.Should().Be(StepOutcome.Applied);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Contact_SignatureInvalid_RejectsWithSignatureMismatch_RegardlessOfOtherChecks()
    {
        var checks = AllPass with { SignatureValid = false };
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: false, checks);
        result.Outcome.Should().Be(StepOutcome.Rejected);
        result.Reason.Should().Be(PhoneVerificationFailureReason.SignatureMismatch);
    }

    [Fact]
    public void Contact_ForeignContact_RejectsWithContactNotOwnedBySender_TheMainSecurityScenario()
    {
        // US-12-04: this is the "sent someone else's contact from the address book" attack the whole
        // cycle exists to close. Signature can be perfectly valid (the platform DID sign this vCard) —
        // ownership is what fails.
        var checks = AllPass with { ContactOwnedBySender = false };
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: false, checks);
        result.Outcome.Should().Be(StepOutcome.Rejected);
        result.Reason.Should().Be(PhoneVerificationFailureReason.ContactNotOwnedBySender);
    }

    [Fact]
    public void Contact_NoUsablePhone_RejectsWithNoPhoneInContact()
    {
        var checks = AllPass with { HasUsablePhone = false };
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: false, checks);
        result.Reason.Should().Be(PhoneVerificationFailureReason.NoPhoneInContact);
    }

    [Fact]
    public void Contact_PhoneDoesNotMatchSession_RejectsWithPhoneMismatch()
    {
        var checks = AllPass with { PhoneMatches = false };
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: false, checks);
        result.Reason.Should().Be(PhoneVerificationFailureReason.PhoneMismatch);
    }

    [Fact]
    public void Contact_LimitReached_OnlyMattersWhenEveryEarlierCheckPasses()
    {
        var checks = AllPass with { LimitReached = true };
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: false, checks);
        result.Reason.Should().Be(PhoneVerificationFailureReason.MaxAccountLimitReached);
    }

    [Fact]
    public void Contact_LimitReachedButSignatureAlsoInvalid_SignatureMismatchWinsByCheckOrder()
    {
        var checks = new ContactChecks(SignatureValid: false, ContactOwnedBySender: true, HasUsablePhone: true, PhoneMatches: true, LimitReached: true);
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: false, checks);
        result.Reason.Should().Be(PhoneVerificationFailureReason.SignatureMismatch);
    }

    [Fact]
    public void Contact_SessionAlreadyVerified_IsIdempotentNoOp()
    {
        var result = EvaluateContact(PhoneVerificationStatus.Verified, expired: false, AllPass);
        result.Outcome.Should().Be(StepOutcome.NoOp);
    }

    [Theory]
    [InlineData(PhoneVerificationStatus.Rejected)]
    [InlineData(PhoneVerificationStatus.Cancelled)]
    [InlineData(PhoneVerificationStatus.Consumed)]
    public void Contact_TerminalSession_IsNoOp_DuplicateDeliverySafe(PhoneVerificationStatus status) =>
        EvaluateContact(status, expired: false, AllPass).Outcome.Should().Be(StepOutcome.NoOp);

    [Fact]
    public void Contact_PendingSession_RejectsWithPayloadUnknown()
    {
        var result = EvaluateContact(PhoneVerificationStatus.Pending, expired: false, AllPass);
        result.Outcome.Should().Be(StepOutcome.Rejected);
        result.Reason.Should().Be(PhoneVerificationFailureReason.PayloadUnknown);
    }

    [Fact]
    public void Contact_ExpiredLinkedSession_RejectsWithPayloadExpired()
    {
        var result = EvaluateContact(PhoneVerificationStatus.Linked, expired: true, AllPass);
        result.Outcome.Should().Be(StepOutcome.Rejected);
        result.Reason.Should().Be(PhoneVerificationFailureReason.PayloadExpired);
    }
}
