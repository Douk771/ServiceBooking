using System.Net;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications.WebPush;

namespace ServiceBooking.UnitTests;

/// <summary>Pure status-code table coverage for <see cref="WebPushResponseClassifier.Classify"/>
/// (ARCHITECTURE_CYCLE9.md §105.8, code-review finding N7).</summary>
public class WebPushResponseClassifierTests
{
    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public void Classify_GoneStatuses_ReturnGone(int statusCode)
    {
        var outcome = WebPushResponseClassifier.Classify((HttpStatusCode)statusCode, null);
        outcome.Should().BeOfType<WebPushSendOutcome.Gone>();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Classify_AuthStatuses_ReturnAuthRejected(int statusCode)
    {
        var outcome = WebPushResponseClassifier.Classify((HttpStatusCode)statusCode, "detail");
        outcome.Should().BeOfType<WebPushSendOutcome.AuthRejected>();
    }

    [Fact]
    public void Classify_413_ReturnsPayloadTooLarge()
    {
        var outcome = WebPushResponseClassifier.Classify((HttpStatusCode)413, "detail");
        outcome.Should().BeOfType<WebPushSendOutcome.PayloadTooLarge>();
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(408)]
    [InlineData(499)]
    public void Classify_TransientStatuses_ReturnTransient(int statusCode)
    {
        // N7: 408 Request Timeout and 499 Client Closed Request are about the REQUEST timing out or
        // being abandoned, not about auth/payload/subscription existence — retrying them is exactly as
        // appropriate as retrying a 5xx, so they must not fall into the AuthRejected default below.
        var outcome = WebPushResponseClassifier.Classify((HttpStatusCode)statusCode, null);
        outcome.Should().BeOfType<WebPushSendOutcome.Transient>();
    }

    [Fact]
    public void Classify_UnrecognizedOther4xx_FallsBackToAuthRejected()
    {
        var outcome = WebPushResponseClassifier.Classify((HttpStatusCode)451, "detail");
        outcome.Should().BeOfType<WebPushSendOutcome.AuthRejected>();
    }
}
