using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>Cycle-12 review, finding 13: unit coverage for the predicates that sit exactly where the
/// review's two blockers lived (the change-phone gate always answering 409, and the mirror being written
/// on a number the account doesn't hold yet) — see PhoneVerificationSessionAcceptance's own doc.</summary>
public class PhoneVerificationSessionAcceptanceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PhoneVerificationSession MakeSession(
        PhoneVerificationStatus status = PhoneVerificationStatus.Verified,
        PhoneVerificationPurpose purpose = PhoneVerificationPurpose.Profile,
        string? userId = "user-1",
        string canonicalPhone = "+79001234567",
        DateTime? consumableUntilUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        Purpose = purpose,
        UserId = userId,
        CanonicalPhone = canonicalPhone,
        ConsumableUntilUtc = consumableUntilUtc ?? Now.AddMinutes(10),
    };

    // ── IsUsableForChangePhone ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChangePhone_AllFiveConditionsSatisfied_ReturnsTrue()
    {
        var session = MakeSession();
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeTrue();
    }

    [Fact]
    public void ChangePhone_NullCandidate_ReturnsFalse() =>
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(null, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();

    [Fact]
    public void ChangePhone_TokenDoesNotMatch_ReturnsFalse()
    {
        var session = MakeSession();
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: false, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void ChangePhone_SessionAlreadyConsumed_ReturnsFalse()
    {
        // The exact bug behind review blocker 1: the webhook used to consume a Profile session the
        // instant `contact` arrived, regardless of which number it was for — this asserts the gate
        // correctly refuses a Consumed session (it must never be presentable) so that the FIX has to be
        // in the webhook (not consuming this session in the first place), not in this predicate.
        var session = MakeSession(status: PhoneVerificationStatus.Consumed);
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void ChangePhone_SessionOnlyLinkedNotYetVerified_ReturnsFalse()
    {
        var session = MakeSession(status: PhoneVerificationStatus.Linked);
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void ChangePhone_WrongPurpose_ReturnsFalse()
    {
        var session = MakeSession(purpose: PhoneVerificationPurpose.Registration);
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void ChangePhone_BelongsToAnotherAccount_ReturnsFalse()
    {
        var session = MakeSession(userId: "someone-else");
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void ChangePhone_VerifiedForADifferentNumber_ReturnsFalse()
    {
        var session = MakeSession(canonicalPhone: "+79007654321");
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void ChangePhone_ConsumableWindowExpired_ReturnsFalse()
    {
        var session = MakeSession(consumableUntilUtc: Now.AddMinutes(-1));
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void ChangePhone_ConsumableWindowExactlyNow_ReturnsFalse()
    {
        // ConsumableUntilUtc must be STRICTLY after now — the boundary itself does not count.
        var session = MakeSession(consumableUntilUtc: Now);
        PhoneVerificationSessionAcceptance.IsUsableForChangePhone(session, tokenMatches: true, "user-1", "+79001234567", Now)
            .Should().BeFalse();
    }

    // ── IsUsableForRegistration ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Registration_AllConditionsSatisfied_ReturnsTrue()
    {
        var session = MakeSession(purpose: PhoneVerificationPurpose.Registration, userId: null);
        PhoneVerificationSessionAcceptance.IsUsableForRegistration(session, tokenMatches: true, "+79001234567", Now)
            .Should().BeTrue();
    }

    [Fact]
    public void Registration_AlreadyAttachedToAnAccount_ReturnsFalse()
    {
        var session = MakeSession(purpose: PhoneVerificationPurpose.Registration, userId: "user-1");
        PhoneVerificationSessionAcceptance.IsUsableForRegistration(session, tokenMatches: true, "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void Registration_WrongPurpose_ReturnsFalse()
    {
        var session = MakeSession(purpose: PhoneVerificationPurpose.Profile, userId: null);
        PhoneVerificationSessionAcceptance.IsUsableForRegistration(session, tokenMatches: true, "+79001234567", Now)
            .Should().BeFalse();
    }

    [Fact]
    public void Registration_PhoneDoesNotMatch_ReturnsFalse()
    {
        var session = MakeSession(purpose: PhoneVerificationPurpose.Registration, userId: null, canonicalPhone: "+79007654321");
        PhoneVerificationSessionAcceptance.IsUsableForRegistration(session, tokenMatches: true, "+79001234567", Now)
            .Should().BeFalse();
    }

    // ── IsOwnCurrentNumberConfirmation ──────────────────────────────────────────────────────────────

    [Fact]
    public void OwnNumberConfirmation_SessionPhoneMatchesAccountPhone_ReturnsTrue() =>
        PhoneVerificationSessionAcceptance.IsOwnCurrentNumberConfirmation("+79001234567", "+79001234567")
            .Should().BeTrue();

    [Fact]
    public void OwnNumberConfirmation_SessionPhoneIsANewNumber_ReturnsFalse()
    {
        // The exact scenario behind review blocker 2 (change-phone gate, US-14-17): the session was
        // opened for a number the account does NOT hold yet — must not be treated as "confirm my own
        // number", or the mirror would falsely light up on a number the account may never actually get.
        PhoneVerificationSessionAcceptance.IsOwnCurrentNumberConfirmation("+79001234567", "+79007654321")
            .Should().BeFalse();
    }

    [Fact]
    public void OwnNumberConfirmation_NoAccount_ReturnsFalse() =>
        PhoneVerificationSessionAcceptance.IsOwnCurrentNumberConfirmation("+79001234567", null)
            .Should().BeFalse();
}
