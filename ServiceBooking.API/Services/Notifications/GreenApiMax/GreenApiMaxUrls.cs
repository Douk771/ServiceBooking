namespace ServiceBooking.API.Services.Notifications.GreenApiMax;

/// <summary>
/// Builds every URL this adapter calls, and the redacted label allowed to reach a log line for it — the
/// MAX counterpart of <see cref="GreenApi.GreenApiUrls"/>. B1 (ARCHITECTURE_CYCLE9.md §104.1/§104.9):
/// GREEN-API's own docs confirm the MAX product ("GREEN-API: MAX", <c>green-api.com/v3/docs/</c>) is
/// built on the SAME instance-based URL shape as WhatsApp — <c>waInstance{id}/{method}/{token}</c>, same
/// method names (<c>sendMessage</c>, <c>getStateInstance</c>, <c>qr</c>, <c>logout</c>,
/// <c>setSettings</c>, <c>getSettings</c>), even the SAME optional <c>/v3/</c> path prefix being
/// unnecessary ("методы можно вызывать одинаково для API мессенджеров WhatsApp, Telegram и MAX"). This
/// class is a SEPARATE type from <see cref="GreenApi.GreenApiUrls"/> anyway (not a thin wrapper around
/// it) — R2: MAX is a distinct GREEN-API product/account with its own partner token and (in principle) a
/// different <c>apiUrl</c>, and a future divergence in method shape must be a change to ONE adapter, not
/// an if-branch inside a shared one.
/// </summary>
public static class GreenApiMaxUrls
{
    public static (Uri Request, string SafeLabel) SendMessage(string apiUrl, string instanceId, string token) =>
        Instance(apiUrl, instanceId, token, "sendMessage");

    public static (Uri Request, string SafeLabel) GetStateInstance(string apiUrl, string instanceId, string token) =>
        Instance(apiUrl, instanceId, token, "getStateInstance");

    public static (Uri Request, string SafeLabel) GetQr(string apiUrl, string instanceId, string token) =>
        Instance(apiUrl, instanceId, token, "qr");

    public static (Uri Request, string SafeLabel) Logout(string apiUrl, string instanceId, string token) =>
        Instance(apiUrl, instanceId, token, "logout");

    public static (Uri Request, string SafeLabel) SetSettings(string apiUrl, string instanceId, string token) =>
        Instance(apiUrl, instanceId, token, "setSettings");

    /// <summary>Instance settings, including <c>wid</c> — confirmed by B1 (the provider's own
    /// <c>outgoingMessageStatus</c> webhook example) to carry the SAME <c>"{phone}@c.us"</c> shape for
    /// MAX as for WhatsApp, despite MAX's chat ids elsewhere being opaque internal numbers
    /// (<c>CheckAccount</c>'s <c>chatId</c>) — <c>wid</c> specifically stays phone-shaped.</summary>
    public static (Uri Request, string SafeLabel) GetSettings(string apiUrl, string instanceId, string token) =>
        Instance(apiUrl, instanceId, token, "getSettings");

    /// <summary>Partner-token call — creates a fresh MAX instance on the platform's OWN, SEPARATE MAX
    /// partner account (B1: GREEN-API confirms "MAX" is billed and provisioned as a distinct product from
    /// WhatsApp — createInstance's response <c>typeInstance</c> is <c>"v3"</c> for MAX vs
    /// <c>"whatsapp"</c>, decided by WHICH partner token called it, not a request parameter).</summary>
    public static (Uri Request, string SafeLabel) CreateInstance(string apiUrl, string partnerToken) =>
        (new Uri($"{Trim(apiUrl)}/partner/createInstance/{Uri.EscapeDataString(partnerToken)}"), "createInstance");

    /// <summary>Partner-token call — deletes a specific MAX instance. Same shape as
    /// <see cref="GreenApi.GreenApiUrls.DeleteInstance"/>: <c>idInstance</c> travels as a NUMBER in the
    /// JSON body, not the path (B7 of the WhatsApp adapter already found the path-based method 404s).</summary>
    public static (Uri Request, string SafeLabel) DeleteInstance(string apiUrl, string partnerToken, string instanceId) =>
        (new Uri($"{Trim(apiUrl)}/partner/deleteInstanceAccount/{Uri.EscapeDataString(partnerToken)}"),
            $"deleteInstanceAccount waInstance{instanceId}");

    /// <summary>
    /// B1: GREEN-API's own "important differences of v3" doc confirms <c>phoneNumber@c.us</c> is still
    /// accepted as a chat id for MAX, for backward compatibility with the WhatsApp-shaped API — so the
    /// SAME chatId construction that works for WhatsApp works for MAX. The provider's own recommended
    /// path (call <c>CheckAccount</c> first, use the returned opaque <c>chatId</c>) is NOT used here: it
    /// would add a second round trip to every single send, and the phone-suffixed form is documented as
    /// supported specifically so callers don't have to.
    /// </summary>
    public static string BuildChatId(string canonicalPhone) => $"{canonicalPhone}@c.us";

    private static (Uri, string) Instance(string apiUrl, string instanceId, string token, string method) =>
        (new Uri($"{Trim(apiUrl)}/waInstance{instanceId}/{method}/{Uri.EscapeDataString(token)}"),
            $"{method} waInstance{instanceId}");

    private static string Trim(string apiUrl) => apiUrl.TrimEnd('/');
}
