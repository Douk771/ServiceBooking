using System.Net;
using System.Net.Sockets;

namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// Connect-time SSRF guard: the resolved address of a push endpoint must be a public unicast address.
/// Complements <see cref="PushEndpointValidator"/>, which only sees the host-name string (DNS-to-internal bypass).
/// </summary>
public static class PushAddressGuard
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
            var b6 = address.GetAddressBytes();
            if ((b6[0] & 0xFE) == 0xFC) return false; // fc00::/7 unique local
            return IsPublicIpv6(b6);
        }
        return IsPublicIpv4(address.GetAddressBytes());
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE25.md §507.1 (T-25-01). An IPv6 address that CARRIES an IPv4 one (translation prefixes) is judged by the embedded IPv4;
    /// prefixes where the embedded address cannot be reliably extracted (NAT64 local-use, Teredo) or that are not routable are rejected whole.
    /// </summary>
    private static bool IsPublicIpv6(byte[] b)
    {
        static bool ZeroRun(byte[] x, int from, int to) { for (var i = from; i < to; i++) if (x[i] != 0) return false; return true; }
        static bool Embedded(byte[] x, int offset) => IsPublicIpv4([x[offset], x[offset + 1], x[offset + 2], x[offset + 3]]);

        // 64:ff9b:1::/48 NAT64 local-use (RFC 8215): position of the IPv4 depends on the network prefix length — rejected whole.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4] == 0x00 && b[5] == 0x01) return false;
        // 64:ff9b::/96 NAT64 well-known (RFC 6052).
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && ZeroRun(b, 4, 12)) return Embedded(b, 12);
        // 2002::/16 6to4: bytes 2..5.
        if (b[0] == 0x20 && b[1] == 0x02) return Embedded(b, 2);
        // 2001::/32 Teredo: the client IPv4 is XOR-obfuscated, the protocol is obsolete — rejected whole.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return false;
        // 2001:db8::/32 documentation.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false;
        // 100::/64 discard-only.
        if (b[0] == 0x01 && b[1] == 0x00 && ZeroRun(b, 2, 8)) return false;
        // ::ffff:0:0:0/96 IPv4-translated (RFC 2765): bytes 0..7 zero, 8..9 = ffff, 10..11 zero.
        if (ZeroRun(b, 0, 8) && b[8] == 0xFF && b[9] == 0xFF && b[10] == 0 && b[11] == 0) return Embedded(b, 12);
        // ::/96 IPv4-compatible (:: and ::1 were rejected earlier).
        if (ZeroRun(b, 0, 12)) return Embedded(b, 12);
        return true;
    }

    private static bool IsPublicIpv4(byte[] b) =>
        !(b[0] == 0 || b[0] == 10 || b[0] >= 224
          || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
          || b[0] == 127
          || (b[0] == 169 && b[1] == 254)
          || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
          || (b[0] == 192 && b[1] == 168)
          || (b[0] == 192 && b[1] == 0 && b[2] == 0)
          || (b[0] == 192 && b[1] == 0 && b[2] == 2)      // 192.0.2.0/24 documentation
          || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
          || (b[0] == 198 && b[1] == 51 && b[2] == 100)   // 198.51.100.0/24 documentation
          || (b[0] == 203 && b[1] == 0 && b[2] == 113));  // 203.0.113.0/24 documentation

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException("Push endpoint resolves to a non-public address.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, ctx.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }
}
