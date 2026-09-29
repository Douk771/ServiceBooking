using System.Net;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications.WebPush;
using Xunit;

namespace ServiceBooking.Tests.Tests;

public class PushAddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")] [InlineData("10.0.0.5")] [InlineData("169.254.169.254")] [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")] [InlineData("100.64.0.1")] [InlineData("0.0.0.0")] [InlineData("::1")]
    [InlineData("fe80::1")] [InlineData("fd00::1")] [InlineData("::ffff:10.0.0.1")]
    public void Rejects_non_public(string ip) => PushAddressGuard.IsPublic(IPAddress.Parse(ip)).Should().BeFalse();

    [Theory]
    [InlineData("8.8.8.8")] [InlineData("142.250.1.1")] [InlineData("2a00:1450::1")]
    public void Accepts_public(string ip) => PushAddressGuard.IsPublic(IPAddress.Parse(ip)).Should().BeTrue();
}
