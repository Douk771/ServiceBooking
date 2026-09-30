namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §507.2 (T-25-02) — the primary handler of the "web-push" client, in one testable place. No system proxy: a proxy
/// would resolve and connect on our behalf, bypassing the connect-time address check of <see cref="PushAddressGuard"/> (SSRF). Push services
/// never redirect.
/// </summary>
public static class WebPushHandlerFactory
{
    public static SocketsHttpHandler Create() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        ConnectCallback = PushAddressGuard.ConnectAsync
    };
}
