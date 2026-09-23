using System.Net;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications.WebPush;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE9.md §105.8's classification table — "таблица, а не «если не 2xx, то
/// повторим»". Every branch of the table, exercised directly against the pure classifier.</summary>
public class WebPushResponseClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void Classify_404Or410_IsGone(HttpStatusCode statusCode) =>
        WebPushResponseClassifier.Classify(statusCode, null).Should().BeOfType<WebPushSendOutcome.Gone>();

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Classify_401Or403_IsAuthRejected(HttpStatusCode statusCode) =>
        WebPushResponseClassifier.Classify(statusCode, "detail").Should().BeOfType<WebPushSendOutcome.AuthRejected>();

    [Fact]
    public void Classify_413_IsPayloadTooLarge() =>
        WebPushResponseClassifier.Classify(HttpStatusCode.RequestEntityTooLarge, null)
            .Should().BeOfType<WebPushSendOutcome.PayloadTooLarge>();

    [Fact]
    public void Classify_429_IsTransient() =>
        WebPushResponseClassifier.Classify(HttpStatusCode.TooManyRequests, null)
            .Should().BeOfType<WebPushSendOutcome.Transient>();

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void Classify_5xx_IsTransient(HttpStatusCode statusCode) =>
        WebPushResponseClassifier.Classify(statusCode, null).Should().BeOfType<WebPushSendOutcome.Transient>();

    [Fact]
    public void Classify_UnexpectedOther4xx_FallsBackToAuthRejected_NotSilentlyRetriedForever() =>
        WebPushResponseClassifier.Classify(HttpStatusCode.Conflict, null)
            .Should().BeOfType<WebPushSendOutcome.AuthRejected>();

    [Fact]
    public void Classify_PreservesResponseBodyAsDetail()
    {
        var outcome = WebPushResponseClassifier.Classify(HttpStatusCode.Forbidden, "vapid mismatch");
        outcome.Should().BeOfType<WebPushSendOutcome.AuthRejected>()
            .Which.Detail.Should().Be("vapid mismatch");
    }
}
