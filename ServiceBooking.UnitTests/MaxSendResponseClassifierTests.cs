using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §499.3 — one file that decides what a MAX answer means for a queue row.</summary>
public class MaxSendResponseClassifierTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public void Success_IsSent(int code) => MaxSendResponseClassifier.Classify(code).Should().BeOfType<MaxSendOutcome.Sent>();

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public void ForbiddenOrNotFound_IsChatUnavailable(int code) =>
        MaxSendResponseClassifier.Classify(code).Should().BeEquivalentTo(new MaxSendOutcome.ChatUnavailable(code));

    [Fact]
    public void TooManyRequests_IsRateLimited() => MaxSendResponseClassifier.Classify(429).Should().BeOfType<MaxSendOutcome.RateLimited>();

    [Theory]
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void ServerSide_IsTransient(int code) => MaxSendResponseClassifier.Classify(code).Should().BeOfType<MaxSendOutcome.Transient>();

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(422)]
    public void ClientConfiguration_IsRejected(int code) =>
        MaxSendResponseClassifier.Classify(code).Should().BeOfType<MaxSendOutcome.Rejected>().Which.StatusCode.Should().Be(code);

    [Fact]
    public void Detail_NeverCarriesMoreThanTheStatus() =>
        MaxSendResponseClassifier.Classify(401).Should().BeOfType<MaxSendOutcome.Rejected>().Which.Detail.Should().Be("HTTP 401");
}
