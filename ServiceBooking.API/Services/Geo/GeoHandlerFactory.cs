using System.Net;
using System.Net.Sockets;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.API.Services.Geo;

/// <summary>
/// Builds the <see cref="SocketsHttpHandler"/> registered as the "yandex-geocoder" named client's primary
/// handler (ARCHITECTURE_CYCLE13.md §206) — a small, purpose-built twin of
/// <c>Notifications.GreenApi.GreenApiHandlerFactory</c>, not a generalisation of it: that factory is
/// parametrized on GREEN-API's own options type, and coupling two unrelated external services through one
/// class is worse than repeating ~20 lines (§206's own remarks). Reuses the ALREADY-TESTED
/// <see cref="PreferIPv4.Order"/> — same boxed-machine "no global IPv6 route" reasoning, same fix.
/// </summary>
public static class GeoHandlerFactory
{
    public static SocketsHttpHandler Create(GeoOptions.YandexOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 16,
            ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
        };

        if (string.Equals(options.ConnectPreference, "System", StringComparison.OrdinalIgnoreCase))
            return handler;

        var perAddressTimeout = TimeSpan.FromSeconds(options.PerAddressConnectTimeoutSeconds);

        handler.ConnectCallback = async (context, ct) =>
        {
            var resolved = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var ordered = PreferIPv4.Order(resolved);
            if (ordered.Count == 0)
                throw new SocketException((int)SocketError.HostNotFound);

            foreach (var address in ordered)
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(perAddressTimeout);

                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), attemptCts.Token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                    socket.Dispose();
                    if (ct.IsCancellationRequested) throw;
                }
            }

            throw new SocketException((int)SocketError.HostUnreachable);
        };

        return handler;
    }
}
