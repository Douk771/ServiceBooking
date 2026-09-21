using System.Net;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.GreenApi;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §28, US-27 p.11 — pure classification, no sockets.</summary>
public class GreenApiResultClassifierTests
{
    [Fact]
    public void Classify_NetworkFailure_IsTransient()
    {
        var outcome = GreenApiResultClassifier.Classify(networkFailure: true, statusCode: null, responseBody: null);
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Fact]
    public void Classify_NoStatusCode_IsTransient()
    {
        var outcome = GreenApiResultClassifier.Classify(networkFailure: false, statusCode: null, responseBody: null);
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Fact]
    public void Classify_429_IsTransient_NotAFailure()
    {
        var outcome = GreenApiResultClassifier.Classify(false, (HttpStatusCode)429, "{}");
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Classify_5xx_IsTransient(HttpStatusCode statusCode)
    {
        var outcome = GreenApiResultClassifier.Classify(false, statusCode, "{}");
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Classify_AuthErrors_AreChannelInvalid(HttpStatusCode statusCode)
    {
        var outcome = GreenApiResultClassifier.Classify(false, statusCode, "{}");
        outcome.Should().BeOfType<SendOutcome.ChannelInvalid>();
    }

    [Fact]
    public void Classify_200WithIdMessage_IsSent()
    {
        var outcome = GreenApiResultClassifier.Classify(false, HttpStatusCode.OK, """{"idMessage":"ABC123"}""");
        var sent = outcome.Should().BeOfType<SendOutcome.Sent>().Subject;
        sent.ProviderMessageId.Should().Be("ABC123");
    }

    [Fact]
    public void Classify_200WithoutIdMessage_IsTransient()
    {
        var outcome = GreenApiResultClassifier.Classify(false, HttpStatusCode.OK, "{}");
        outcome.Should().BeOfType<SendOutcome.TransientFailure>();
    }

    [Fact]
    public void Classify_200WithNoAccount_IsPermanentlyRejected()
    {
        var outcome = GreenApiResultClassifier.Classify(false, HttpStatusCode.OK, """{"noAccount":true}""");
        var rejected = outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>().Subject;
        rejected.Reason.Should().Be(NotificationReason.RecipientHasNoWhatsApp);
    }

    [Fact]
    public void Classify_400WithNoAccountText_IsPermanentlyRejected()
    {
        var outcome = GreenApiResultClassifier.Classify(false, HttpStatusCode.BadRequest, """{"error":"noAccount"}""");
        var rejected = outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>().Subject;
        rejected.Reason.Should().Be(NotificationReason.RecipientHasNoWhatsApp);
    }

    [Fact]
    public void Classify_400WithUnauthorizedInstanceText_IsChannelInvalid()
    {
        var outcome = GreenApiResultClassifier.Classify(false, HttpStatusCode.BadRequest, """{"error":"notAuthorized"}""");
        outcome.Should().BeOfType<SendOutcome.ChannelInvalid>();
    }

    [Fact]
    public void Classify_400Otherwise_IsPermanentlyRejectedWithGenericReason()
    {
        var outcome = GreenApiResultClassifier.Classify(false, HttpStatusCode.BadRequest, """{"error":"something else"}""");
        var rejected = outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>().Subject;
        rejected.Reason.Should().Be(NotificationReason.RejectedByProvider);
    }

    [Fact]
    public void Classify_MalformedJsonBody_DoesNotThrow_TreatsAsGenericRejection()
    {
        var outcome = GreenApiResultClassifier.Classify(false, HttpStatusCode.BadRequest, "not json at all");
        outcome.Should().BeOfType<SendOutcome.PermanentlyRejected>();
    }
}
