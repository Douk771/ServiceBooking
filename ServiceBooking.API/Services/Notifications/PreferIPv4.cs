using System.Net;
using System.Net.Sockets;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Orders resolved addresses IPv4-first (ARCHITECTURE_CYCLE4.md §28.1) — NOT a hard
/// <see cref="AddressFamily.InterNetwork"/> filter. The boxed machine has no global IPv6 route (only
/// link-local), and .NET's default socket handler's address enumeration tries addresses in DNS order;
/// when an IPv6 address happens to come first, the connection attempt
/// waits out the OS's own connect timeout (measured ~5s) before falling back — doubling a dispatch pass
/// that already spaces messages by 5–15 seconds (§26).
///
/// Ordering rather than forcing IPv4 matters because the lack of IPv6 is a property of THIS machine
/// TODAY, not a fact about the provider or a future host — a hard filter would produce a confusing
/// failure the day IPv6 becomes IPv4's only option, disconnected from the reasoning that led here a year
/// earlier. Ordering degrades gracefully in that world too: the (now-failing) IPv4 attempts simply lose
/// to the IPv6 attempts that follow them, at the cost of one connect-timeout's worth of latency — see the
/// adapter's <c>ConnectCallback</c> for the per-address timeout that bounds that cost.
/// </summary>
public static class PreferIPv4
{
    /// <summary>Stable partition: every IPv4 address first (in its original relative order), then every
    /// IPv6 address (in its original relative order). Safe on empty, IPv4-only, and IPv6-only input.</summary>
    public static IReadOnlyList<IPAddress> Order(IReadOnlyList<IPAddress> addresses) =>
        [
            .. addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork),
            .. addresses.Where(a => a.AddressFamily != AddressFamily.InterNetwork),
        ];
}
