using System.Net;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications.WebPush;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §507.1 (T-25-01) — IPv6 wrappers of IPv4 are judged by the embedded address; Teredo and NAT64 local-use are rejected whole.</summary>
public class PushAddressGuardTests
{
    private static bool Public(string ip) => PushAddressGuard.IsPublic(IPAddress.Parse(ip));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("::ffff:0:8.8.8.8")]          // IPv4-translated
    [InlineData("64:ff9b::8.8.8.8")]          // NAT64
    [InlineData("2002:0808:0808::1")]         // 6to4 of 8.8.8.8
    [InlineData("::8.8.8.8")]                 // IPv4-compatible
    public void PublicAddresses_Pass(string ip) => Public(ip).Should().BeTrue();

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("192.0.2.5")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:0:10.0.0.1")]
    [InlineData("64:ff9b::10.0.0.1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("2002:0a00:0001::1")]         // 6to4 of 10.0.0.1
    [InlineData("2002:c0a8:0101::1")]         // 6to4 of 192.168.1.1
    [InlineData("::10.0.0.1")]
    [InlineData("::a9fe:a9fe")]               // IPv4-compatible 169.254.169.254
    public void PrivateEmbeddedIpv4_IsRejected(string ip) => Public(ip).Should().BeFalse();

    [Theory]
    [InlineData("64:ff9b:1::8.8.8.8")]        // NAT64 local-use, even with a public IPv4 inside
    [InlineData("64:ff9b:1::a00:1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")] // Teredo
    [InlineData("2001::1")]
    [InlineData("2001:db8::1")]
    [InlineData("100::1")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    public void NonRoutableOrUnextractable_IsRejectedWhole(string ip) => Public(ip).Should().BeFalse();
}
