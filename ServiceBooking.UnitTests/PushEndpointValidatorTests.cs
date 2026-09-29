using FluentAssertions;
using ServiceBooking.API.Services.Notifications.WebPush;
using Xunit;

namespace ServiceBooking.UnitTests;

public class PushEndpointValidatorTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://push.example.test/x")]
    public void Accepts_public_https_hosts(string url) => PushEndpointValidator.IsValid(url).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://fcm.googleapis.com/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://2130706433/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://db/x")]
    [InlineData("https://svc.internal/x")]
    [InlineData("https://user:pw@fcm.googleapis.com/x")]
    [InlineData("https://fcm.googleapis.com:6379/x")]
    public void Rejects_unsafe(string? url) => PushEndpointValidator.IsValid(url).Should().BeFalse();
}
