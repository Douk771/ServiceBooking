using System.Net;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.GreenApiMax;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE9.md §104.9 (B1/B4) — pure classification, no sockets. Mirrors
/// GreenApiResultClassifierTests; the MAX-specific differences are the last few cases (RecipientNotInMax
/// instead of RecipientHasNoWhatsApp, and the "suspended"/pendingPassword body-text variant of
/// ChannelInvalid).</summary>
public class GreenApiMaxResultClassifierTests
{
    [Fact]
    public void Classify_NetworkFailure_IsTransient()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(networkFailure: true, statusCode: null, responseBody: null);
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Fact]
    public void Classify_NoStatusCode_IsTransient()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(networkFailure: false, statusCode: null, responseBody: null);
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Fact]
    public void Classify_429_IsTransient_NotAFailure()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, (HttpStatusCode)429, "{}");
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Classify_5xx_IsTransient(HttpStatusCode statusCode)
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, statusCode, "{}");
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Classify_AuthErrors_AreChannelInvalid(HttpStatusCode statusCode)
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, statusCode, "{}");
        outcome.Should().BeOfType<SendOutcome.ChannelInvalid>();
    }

    // B1: MAX's "Your account is suspended" is a 403 — already covered by the AuthErrors case above
    // (403 -> ChannelInvalid regardless of body), but pinned explicitly here so a future refactor of the
    // status-code branch can't silently stop covering the documented MAX wording.
    [Fact]
    public void Classify_403SuspendedAccount_IsChannelInvalid()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.Forbidden, """{"message":"Your account is suspended"}""");
        outcome.Should().BeOfType<SendOutcome.ChannelInvalid>();
    }

    [Fact]
    public void Classify_200WithIdMessage_IsSent()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.OK, """{"idMessage":"ABC123"}""");
        var sent = outcome.Should().BeOfType<SendOutcome.Sent>().Subject;
        sent.ProviderMessageId.Should().Be("ABC123");
    }

    [Fact]
    public void Classify_200WithoutIdMessage_IsTransient()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.OK, "{}");
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Fact]
    public void Classify_200WithNoAccount_IsPermanentlyRejectedAsRecipientNotInMax()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.OK, """{"noAccount":true}""");
        var rejected = outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>().Subject;
        rejected.Reason.Should().Be(NotificationReason.RecipientNotInMax);
    }

    [Fact]
    public void Classify_400WithNoAccountText_IsPermanentlyRejectedAsRecipientNotInMax()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.BadRequest, """{"error":"noAccount"}""");
        var rejected = outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>().Subject;
        rejected.Reason.Should().Be(NotificationReason.RecipientNotInMax);
    }

    [Fact]
    public void Classify_400WithUnauthorizedInstanceText_IsChannelInvalid()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.BadRequest, """{"error":"notAuthorized"}""");
        outcome.Should().BeOfType<SendOutcome.ChannelInvalid>();
    }

    // B1: pendingPassword (MAX's 2FA-not-completed state) is treated the same as an unauthorized
    // instance — this cycle doesn't implement SendAuthorizationPassword, so it must not be retried as a
    // per-message rejection.
    [Fact]
    public void Classify_400WithPendingPasswordText_IsChannelInvalid()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.BadRequest, """{"error":"pendingPassword"}""");
        outcome.Should().BeOfType<SendOutcome.ChannelInvalid>();
    }

    [Fact]
    public void Classify_400Otherwise_IsPermanentlyRejectedWithGenericReason()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.BadRequest, """{"error":"something else"}""");
        var rejected = outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>().Subject;
        rejected.Reason.Should().Be(NotificationReason.RejectedByProvider);
    }

    [Fact]
    public void Classify_MalformedJsonBody_DoesNotThrow_TreatsAsGenericRejection()
    {
        var outcome = GreenApiMaxResultClassifier.Classify(false, HttpStatusCode.BadRequest, "not json at all");
        outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>();
    }
}
