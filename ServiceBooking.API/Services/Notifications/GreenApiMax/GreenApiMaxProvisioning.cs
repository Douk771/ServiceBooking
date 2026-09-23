using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services;

namespace ServiceBooking.API.Services.Notifications.GreenApiMax;

/// <summary>Thrown by every <see cref="GreenApiMaxProvisioning"/> call that fails — mirrors
/// <see cref="GreenApi.GreenApiProvisioningException"/> exactly (message built from <c>SafeLabel</c> and
/// the status code only, never the raw body/URL).</summary>
public sealed class GreenApiMaxProvisioningException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Everything that uses the PLATFORM's own MAX partner token (ARCHITECTURE_CYCLE9.md §104.1/§104.2) —
/// the MAX counterpart of <see cref="GreenApi.GreenApiProvisioning"/>. B1's research (§104.9): GREEN-API's
/// partner methods (<c>createInstance</c>/<c>deleteInstanceAccount</c>) are UNCHANGED for MAX — same
/// request/response shape as WhatsApp's — but they must be called with MAX's OWN partner token
/// (<c>Notifications:GreenApiMax:PartnerToken</c>), because <c>createInstance</c>'s response
/// <c>typeInstance</c> ("v3" for MAX vs "whatsapp") is decided by WHICH partner account the token
/// belongs to, not by anything in the request body — there is no "give me a MAX instance" parameter to
/// set.
/// </summary>
public sealed class GreenApiMaxProvisioning(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<GreenApiMaxProvisioning> logger) : IChannelProvisioning
{
    private const int QrRefreshAfterSeconds = 3;

    public async Task<ProvisionedInstance> CreateInstanceAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiMaxUrls.CreateInstance(opts.GreenApiMax.ApiUrl, opts.GreenApiMax.PartnerToken ?? string.Empty);

        var (body, _) = await SendAsync(HttpMethod.Post, uri, safeLabel, ct, jsonBody: null);

        var idInstance = ReadString(body, "idInstance") ?? ReadNumberAsString(body, "idInstance");
        var apiTokenInstance = ReadString(body, "apiTokenInstance");
        if (idInstance is null || apiTokenInstance is null)
            throw new GreenApiMaxProvisioningException($"{safeLabel}: response did not contain idInstance/apiTokenInstance.");

        // B1: unlike the WhatsApp partner API's UNVERIFIED country field, MAX's own v3 createInstance
        // response schema (green-api.com/v3/docs/partners/createInstance/) documents exactly five
        // response fields — apiTokenInstance, apiUrl, idInstance, mediaUrl, typeInstance — and none of
        // them is a server/country field. ServerCountry is left null rather than guessed at: null already
        // means "nothing to check" to the caller (ProvisionedInstance's own doc comment), the same as it
        // does for a WhatsApp response the provider genuinely didn't report a country on.
        var reportedType = ReadString(body, "typeInstance");
        if (reportedType is not null && !string.Equals(reportedType, "v3", StringComparison.OrdinalIgnoreCase))
            logger.LogWarning(
                "GREEN-API MAX createInstance returned typeInstance={ReportedType} (expected \"v3\") for a MAX partner token — " +
                "the instance may have been created under the wrong product.", reportedType);

        return new ProvisionedInstance(idInstance, apiTokenInstance, ServerCountry: null);
    }

    public async Task<QrSnapshot> GetQrAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiMaxUrls.GetQr(opts.GreenApiMax.ApiUrl, credentials.InstanceId, credentials.Token);
        var (body, _) = await SendAsync(HttpMethod.Get, uri, safeLabel, ct);

        var type = ReadString(body, "type");
        switch (type)
        {
            case "qrCode":
                return new QrSnapshot(ReadString(body, "message"), Authorized: false, QrRefreshAfterSeconds);

            case "alreadyLogged":
                var phoneNumber = await GetPhoneNumberAsync(credentials, ct);
                return new QrSnapshot(null, Authorized: true, QrRefreshAfterSeconds, phoneNumber);

            default:
                // Covers "still starting" AND the case this cycle deliberately doesn't implement —
                // stateInstance == pendingPassword (a login password set on the MAX account, requiring
                // SendAuthorizationPassword — not built this cycle, ARCHITECTURE_CYCLE9.md §104.1). The
                // connect screen's own connectionNotice tells the owner to disable that password BEFORE
                // starting (US-119's acceptance criterion); if they didn't, the QR poll simply never
                // reaches "alreadyLogged" and ChannelHealthTask's existing stuck-in-Connecting timeout
                // (Notifications:UnauthorizedInstanceTimeoutMinutes) eventually abandons the attempt —
                // the same outcome an ordinary unscanned QR gets, not a new failure mode to build.
                return new QrSnapshot(null, Authorized: false, QrRefreshAfterSeconds);
        }
    }

    /// <summary>B1: confirmed via the provider's own <c>outgoingMessageStatus</c> webhook example —
    /// <c>wid</c> stays phone-shaped (<c>"79991234567@c.us"</c>) for MAX, unlike the opaque numeric chat
    /// ids <c>CheckAccount</c> returns elsewhere — so this is a straight copy of
    /// <see cref="GreenApi.GreenApiProvisioning.GetPhoneNumberAsync"/>'s logic.</summary>
    public async Task<string?> GetPhoneNumberAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        try
        {
            var opts = options.Value;
            var (uri, safeLabel) = GreenApiMaxUrls.GetSettings(opts.GreenApiMax.ApiUrl, credentials.InstanceId, credentials.Token);
            var (body, _) = await SendAsync(HttpMethod.Get, uri, safeLabel, ct);

            var wid = ReadString(body, "wid");
            if (string.IsNullOrEmpty(wid)) return null;

            var rawPhone = wid.Split('@', 2)[0];
            return PhoneNormalizer.TryNormalize(rawPhone, out var canonical) ? canonical : null;
        }
        catch (GreenApiMaxProvisioningException ex)
        {
            logger.LogWarning(ex, "Failed to fetch MAX phone number via getSettings after QR authorization, will retry on next poll");
            return null;
        }
    }

    public async Task<ProviderChannelState> GetStateAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiMaxUrls.GetStateInstance(opts.GreenApiMax.ApiUrl, credentials.InstanceId, credentials.Token);
        var (body, _) = await SendAsync(HttpMethod.Get, uri, safeLabel, ct);
        return GreenApiMaxStateInstanceParser.Parse(ReadString(body, "stateInstance"));
    }

    public async Task ConfigureInstanceAsync(ChannelCredentials credentials, int sendDelayMilliseconds, string? webhookUrl, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiMaxUrls.SetSettings(opts.GreenApiMax.ApiUrl, credentials.InstanceId, credentials.Token);

        var body = new Dictionary<string, object>
        {
            ["delaySendMessagesMilliseconds"] = sendDelayMilliseconds,
        };
        if (webhookUrl is not null)
        {
            // Same field names as the WhatsApp adapter's setSettings call — B1 confirmed the "important
            // differences" doc lists webhook plumbing under "не изменились" (unchanged).
            body["webhookUrl"] = webhookUrl;
            body["outgoingAPIMessageWebhook"] = "yes";
            body["stateWebhook"] = "yes";
            body["incomingWebhook"] = "no";
        }

        try
        {
            await SendAsync(HttpMethod.Post, uri, safeLabel, ct, body);
        }
        catch (GreenApiMaxProvisioningException ex)
        {
            logger.LogWarning(ex, "Configuring GREEN-API MAX instance settings (send delay/webhook) failed, continuing without it");
        }
    }

    public async Task LogoutAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiMaxUrls.Logout(opts.GreenApiMax.ApiUrl, credentials.InstanceId, credentials.Token);
        try
        {
            await SendAsync(HttpMethod.Get, uri, safeLabel, ct);
        }
        catch (GreenApiMaxProvisioningException ex)
        {
            logger.LogWarning(ex, "GREEN-API MAX logout failed, continuing with instance deletion");
        }
    }

    public async Task<InstanceDeletion> DeleteInstanceAsync(string instanceId, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiMaxUrls.DeleteInstance(opts.GreenApiMax.ApiUrl, opts.GreenApiMax.PartnerToken ?? string.Empty, instanceId);

        if (!long.TryParse(instanceId, out var numericInstanceId))
            throw new GreenApiMaxProvisioningException($"{safeLabel}: instance id \"{instanceId}\" is not numeric.");

        HttpStatusCode? statusCode;
        string? body;
        try
        {
            (body, statusCode) = await SendAsync(HttpMethod.Post, uri, safeLabel, ct, new { idInstance = numericInstanceId });
        }
        catch (GreenApiMaxProvisioningException)
        {
            return new InstanceDeletion(Success: false);
        }

        var isSuccess = ReadBool(body, "isSuccess") ?? ReadBool(body, "result");
        if (statusCode == HttpStatusCode.NotFound)
            return new InstanceDeletion(Success: BodyIndicatesInstanceAlreadyGone(body));

        return new InstanceDeletion(Success: isSuccess ?? statusCode is HttpStatusCode.OK);
    }

    private static bool BodyIndicatesInstanceAlreadyGone(string? body)
    {
        var message = ReadString(body ?? string.Empty, "message") ?? ReadString(body ?? string.Empty, "error");
        return message is not null &&
               (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("instance not exists", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(string Body, HttpStatusCode StatusCode)> SendAsync(
        HttpMethod method, Uri uri, string safeLabel, CancellationToken ct, object? jsonBody = null)
    {
        var client = httpClientFactory.CreateClient("green-api");
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (jsonBody is not null) request.Content = JsonContent.Create(jsonBody);
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogInformation("GREEN-API MAX request {SafeLabel} took {DurationMs}ms, status=(network failure)",
                safeLabel, stopwatch.ElapsedMilliseconds);
            throw new GreenApiMaxProvisioningException($"{safeLabel}: network failure.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger.LogInformation("GREEN-API MAX request {SafeLabel} took {DurationMs}ms, status=(timeout)",
                safeLabel, stopwatch.ElapsedMilliseconds);
            throw new GreenApiMaxProvisioningException($"{safeLabel}: request timed out.", ex);
        }

        using (response)
        {
            stopwatch.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogInformation("GREEN-API MAX request {SafeLabel} took {DurationMs}ms, status={StatusCode}",
                safeLabel, stopwatch.ElapsedMilliseconds, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new GreenApiMaxProvisioningException($"{safeLabel}: provider returned {(int)response.StatusCode}.");

            return (body, response.StatusCode);
        }
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadNumberAsString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? ReadBool(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(property, out var value))
                return null;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
