namespace ServiceBooking.API.Services.Notifications.GreenApi;

/// <summary>
/// Builds every URL this adapter calls, and — critically — the redacted label that is allowed to reach
/// a log line for it (ARCHITECTURE_CYCLE4.md §24.3 rung 2). <see cref="Uri"/> carries the real token (it
/// has to, to make the actual HTTP call); <c>SafeLabel</c> never does. No caller of this class should
/// ever interpolate <c>Uri</c> itself into a log message — only <c>SafeLabel</c>.
/// </summary>
public static class GreenApiUrls
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

    /// <summary>Instance settings, including <c>wid</c> — the authorized WhatsApp id, GREEN-API's own
    /// source for the channel's phone number once it's connected (§29.1).</summary>
    public static (Uri Request, string SafeLabel) GetSettings(string apiUrl, string instanceId, string token) =>
        Instance(apiUrl, instanceId, token, "getSettings");

    /// <summary>Partner-token call — creates a fresh instance on the platform's own account, not a
    /// salon's. No instance id exists yet, so the label carries none.</summary>
    public static (Uri Request, string SafeLabel) CreateInstance(string apiUrl, string partnerToken) =>
        (new Uri($"{Trim(apiUrl)}/partner/createInstance/{Uri.EscapeDataString(partnerToken)}"), "createInstance");

    /// <summary>Partner-token call — deletes a specific instance. §30.4: this uses the PLATFORM's
    /// partner token and the bare instance id, never a channel's own (possibly already-blank) token.
    /// B7: the partner method is <c>deleteInstanceAccount</c>, not <c>deleteInstance</c> — the latter
    /// path doesn't exist and returned 404 for every call, which the caller then (wrongly) treated as
    /// "already deleted", so no instance was ever actually removed. <c>idInstance</c> is sent as a NUMBER
    /// in the JSON body, not appended to the path.</summary>
    public static (Uri Request, string SafeLabel) DeleteInstance(string apiUrl, string partnerToken, string instanceId) =>
        (new Uri($"{Trim(apiUrl)}/partner/deleteInstanceAccount/{Uri.EscapeDataString(partnerToken)}"),
            $"deleteInstanceAccount waInstance{instanceId}");

    /// <summary>
    /// The one place a canonical phone number becomes a GREEN-API <c>chatId</c> (US-27 p.1). SPEC is
    /// explicit that the two formats happening to line up is luck, not a guarantee — if a live check ever
    /// shows otherwise, this is the only function that needs to change.
    /// </summary>
    public static string BuildChatId(string canonicalPhone) => $"{canonicalPhone}@c.us";

    private static (Uri, string) Instance(string apiUrl, string instanceId, string token, string method) =>
        (new Uri($"{Trim(apiUrl)}/waInstance{instanceId}/{method}/{Uri.EscapeDataString(token)}"),
            $"{method} waInstance{instanceId}");

    private static string Trim(string apiUrl) => apiUrl.TrimEnd('/');
}
