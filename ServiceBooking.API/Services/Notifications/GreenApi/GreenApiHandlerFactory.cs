using System.Net;
using System.Net.Sockets;

namespace ServiceBooking.API.Services.Notifications.GreenApi;

/// <summary>
/// Builds the <see cref="SocketsHttpHandler"/> registered as the "green-api" named client's primary
/// handler (ARCHITECTURE_CYCLE4.md §28.1) — the two independent fixes for the boxed machine's missing
/// global IPv6 route: connection reuse (this handler's pooling settings) and address ordering (the
/// <see cref="SocketsHttpHandler.ConnectCallback"/>, built on top of the already-tested
/// <see cref="PreferIPv4.Order"/>). Deliberately not unit-tested itself (only <c>PreferIPv4.Order</c> is)
/// — it is verified on the real machine (T4-D3) by reading the request-duration figure this adapter logs
/// alongside every call.
/// </summary>
public static class GreenApiHandlerFactory
{
    public static SocketsHttpHandler Create(NotificationOptions.GreenApiOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            // A channel's messages are 5-15s apart and the dispatcher's own period is 1 minute (§26.1) —
            // a pooled connection easily survives both a whole dispatch pass and the gap until the next
            // one, so a fresh connection (and its IPv4-first probe) is paid for roughly once every couple
            // of minutes per channel instead of on every single send.
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            // A custom primary handler opts the client OUT of IHttpClientFactory's own HandlerLifetime
            // rotation — without setting this explicitly, a long-lived container would pin itself to
            // whatever address/certificate resolved at first use, forever.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            // Double MaxParallelChannels (8, Notifications:Dispatch:MaxParallelChannels) so the pool
            // never has to open/close connections under normal peak load.
            MaxConnectionsPerServer = 16,
            ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
        };

        // Emergency escape hatch (§28.1): "System" fully disables the custom connect logic below and
        // falls back to .NET's own default address selection, in case the callback is ever wrong for a
        // future host. Pooling (above) stays in effect either way — it is useful regardless.
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
                    // The CALLER's own token (request timeout / dispatch budget), not this address's
                    // 2-second probe, was what fired — stop trying further addresses and let it propagate,
                    // rather than spending more time on addresses that will only be abandoned too.
                    if (ct.IsCancellationRequested) throw;
                }
            }

            // Every address's own PerAddressConnectTimeoutSeconds-bounded attempt failed.
            throw new SocketException((int)SocketError.HostUnreachable);
        };

        return handler;
    }
}
