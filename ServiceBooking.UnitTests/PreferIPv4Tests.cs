using System.Net;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §28.1 — pure address ordering, no sockets.</summary>
public class PreferIPv4Tests
{
    private static readonly IPAddress V4A = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress V4B = IPAddress.Parse("192.0.2.2");
    private static readonly IPAddress V6A = IPAddress.Parse("2001:db8::1");
    private static readonly IPAddress V6B = IPAddress.Parse("2001:db8::2");

    [Fact]
    public void Order_EmptyList_ReturnsEmpty()
    {
        PreferIPv4.Order([]).Should().BeEmpty();
    }

    [Fact]
    public void Order_OnlyIPv4_PreservesOrder()
    {
        PreferIPv4.Order([V4B, V4A]).Should().Equal(V4B, V4A);
    }

    [Fact]
    public void Order_OnlyIPv6_PreservesOrder_DoesNotThrow()
    {
        PreferIPv4.Order([V6B, V6A]).Should().Equal(V6B, V6A);
    }

    [Fact]
    public void Order_Mixed_IPv6First_MovesAllIPv4Ahead_StableWithinFamily()
    {
        var result = PreferIPv4.Order([V6A, V4B, V6B, V4A]);
        result.Should().Equal(V4B, V4A, V6A, V6B);
    }

    [Fact]
    public void Order_Mixed_IPv4First_UnaffectedRelativeOrder()
    {
        var result = PreferIPv4.Order([V4A, V6A, V4B, V6B]);
        result.Should().Equal(V4A, V4B, V6A, V6B);
    }
}
