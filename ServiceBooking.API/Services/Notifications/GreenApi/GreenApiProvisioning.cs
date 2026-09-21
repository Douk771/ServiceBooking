using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services;

namespace ServiceBooking.API.Services.Notifications.GreenApi;

/// <summary>The <c>Notifications:Provider</c> config value this adapter answers to. A single named
/// constant, defined here rather than repeated as a string literal at every comparison site (§37's
/// acceptance grep: the provider's name is only allowed to appear inside this folder or in config) —
/// <see cref="DeploymentSafetyChecks"/> references this instead of writing the literal itself.</summary>
public static class GreenApiProviderName
{
    public const string Value = "green-api";
}

/// <summary>Thrown by every <see cref="GreenApiProvisioning"/> call that fails — the message is always
/// built from <c>SafeLabel</c> and the status code, never from the raw response body or the request URL,
/// so a controller that lets this bubble into a log or a 5xx problem+json never leaks a token
/// (ARCHITECTURE_CYCLE4.md §24.3).</summary>
public sealed class GreenApiProvisioningException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Everything that uses the PLATFORM's own partner token (ARCHITECTURE_CYCLE4.md §28). Unlike
/// <see cref="GreenApiTransport"/>, failures here are exceptional rather than classified into a result
/// type — provisioning calls are rare, synchronous-with-a-human-waiting operations (the connect flow,
/// §29), not a high-volume queue the way sending is, so there is no backoff/retry state machine that
/// needs a closed set of outcomes to switch on.
/// </summary>
public sealed class GreenApiProvisioning(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<GreenApiProvisioning> logger) : IChannelProvisioning
{
    // GREEN-API's own recommendation for how often a device may reasonably re-poll its QR endpoint,
    // relayed to the caller as QrSnapshot.RefreshAfterSeconds (§29.2) — the actual client-visible cache
    // in front of this call is IMemoryCache in the connect-flow controller, not this adapter.
    private const int QrRefreshAfterSeconds = 3;

    public async Task<ProvisionedInstance> CreateInstanceAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiUrls.CreateInstance(opts.GreenApi.ApiUrl, opts.PartnerToken ?? string.Empty);
        var (body, _) = await SendAsync(HttpMethod.Post, uri, safeLabel, ct);

        var idInstance = ReadString(body, "idInstance") ?? ReadNumberAsString(body, "idInstance");
        var apiTokenInstance = ReadString(body, "apiTokenInstance");
        if (idInstance is null || apiTokenInstance is null)
            throw new GreenApiProvisioningException($"{safeLabel}: response did not contain idInstance/apiTokenInstance.");

        return new ProvisionedInstance(idInstance, apiTokenInstance);
    }

    public async Task<QrSnapshot> GetQrAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiUrls.GetQr(opts.GreenApi.ApiUrl, credentials.InstanceId, credentials.Token);
        var (body, _) = await SendAsync(HttpMethod.Get, uri, safeLabel, ct);

        var type = ReadString(body, "type");
        switch (type)
        {
            case "qrCode":
                return new QrSnapshot(ReadString(body, "message"), Authorized: false, QrRefreshAfterSeconds);

            case "alreadyLogged":
                // §29.1: the moment the connect flow's poll first sees Authorized is exactly the moment
                // it needs the phone number for NotificationChannel.PhoneNumber — fetched here, from the
                // same round trip, rather than a second provisioning call the controller would have to
                // remember to make.
                var phoneNumber = await GetPhoneNumberAsync(credentials, ct);
                return new QrSnapshot(null, Authorized: true, QrRefreshAfterSeconds, phoneNumber);

            default:
                // Instance still starting up (no QR produced yet) — not an error, just "try again shortly".
                return new QrSnapshot(null, Authorized: false, QrRefreshAfterSeconds);
        }
    }

    /// <summary>GREEN-API's own source for the authorized number: <c>getSettings</c>'s <c>wid</c> field,
    /// shaped like <c>"79991234567@c.us"</c>. Normalized through the same <see cref="PhoneNormalizer"/>
    /// every other phone in the system goes through (US-53 p.6) — never trusted as already-canonical.
    /// A failure here must not fail the whole connect flow (the channel is still genuinely Connected):
    /// <see langword="null"/> is returned, and PhoneNumber simply stays unset until a later poll succeeds.</summary>
    public async Task<string?> GetPhoneNumberAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        try
        {
            var opts = options.Value;
            var (uri, safeLabel) = GreenApiUrls.GetSettings(opts.GreenApi.ApiUrl, credentials.InstanceId, credentials.Token);
            var (body, _) = await SendAsync(HttpMethod.Get, uri, safeLabel, ct);

            var wid = ReadString(body, "wid");
            if (string.IsNullOrEmpty(wid)) return null;

            var rawPhone = wid.Split('@', 2)[0];
            return PhoneNormalizer.TryNormalize(rawPhone, out var canonical) ? canonical : null;
        }
        catch (GreenApiProvisioningException ex)
        {
            logger.LogWarning(ex, "Failed to fetch phone number via getSettings after QR authorization, will retry on next poll");
            return null;
        }
    }

    public async Task<ProviderChannelState> GetStateAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiUrls.GetStateInstance(opts.GreenApi.ApiUrl, credentials.InstanceId, credentials.Token);
        var (body, _) = await SendAsync(HttpMethod.Get, uri, safeLabel, ct);
        return GreenApiStateInstanceParser.Parse(ReadString(body, "stateInstance"));
    }

    public async Task ConfigureInstanceAsync(ChannelCredentials credentials, int sendDelayMilliseconds, string? webhookUrl, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiUrls.SetSettings(opts.GreenApi.ApiUrl, credentials.InstanceId, credentials.Token);

        // N5: ONE setSettings call, not two back-to-back — GREEN-API restarts the instance on every
        // settings change, so a separate delay call followed by a separate webhook call meant the owner
        // watched the instance restart twice in a row right before the QR code appeared.
        var body = new Dictionary<string, object>
        {
            ["delaySendMessagesMilliseconds"] = sendDelayMilliseconds,
        };
        if (webhookUrl is not null)
        {
            // I2: `webhookUrl` already carries our own path token — `webhookUrlToken` is deliberately
            // NOT set (see cycle report). `outgoingAPIMessageWebhook` is delivery-status callbacks for
            // messages sent via `sendMessage` (what NotificationDispatchTask does) specifically, as
            // opposed to `outgoingMessageWebhook` (messages sent from the linked phone itself) — the
            // former is the one this cycle needs. `stateWebhook` is the `stateInstance` push
            // (ChannelStateMapper's second entry point, §32). Incoming-message webhooks stay off: this
            // cycle never reads client replies. Omitted entirely (not sent as empty/false) when
            // webhookUrl is null, so this call never actively clears a webhook some other path set.
            body["webhookUrl"] = webhookUrl;
            body["outgoingAPIMessageWebhook"] = "yes";
            body["stateWebhook"] = "yes";
            body["incomingWebhook"] = "no";
        }

        try
        {
            await SendAsync(HttpMethod.Post, uri, safeLabel, ct, body);
        }
        catch (GreenApiProvisioningException ex)
        {
            // §29.1/I2: best effort — a failure here must not roll back an otherwise-successful Connect.
            // A failed delay means the provider's own throttle stays at its default instead of our 5s
            // floor; a failed webhook means ChannelHealthTask's own poll remains the fallback path for
            // state changes even if the webhook is never actually delivered for this instance.
            logger.LogWarning(ex, "Configuring GREEN-API instance settings (send delay/webhook) failed, continuing without it");
        }
    }

    public async Task LogoutAsync(ChannelCredentials credentials, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiUrls.Logout(opts.GreenApi.ApiUrl, credentials.InstanceId, credentials.Token);
        try
        {
            // B7 / reviewer note: GREEN-API's logout is a GET, not a POST.
            await SendAsync(HttpMethod.Get, uri, safeLabel, ct);
        }
        catch (GreenApiProvisioningException ex)
        {
            // §30.4 step 2: best effort — an instance that is about to be deleted outright does not need
            // a clean logout first, and a failure here must never block the delete that follows.
            logger.LogWarning(ex, "GREEN-API logout failed, continuing with instance deletion");
        }
    }

    public async Task<InstanceDeletion> DeleteInstanceAsync(string instanceId, CancellationToken ct)
    {
        var opts = options.Value;
        var (uri, safeLabel) = GreenApiUrls.DeleteInstance(opts.GreenApi.ApiUrl, opts.PartnerToken ?? string.Empty, instanceId);

        // B7: idInstance travels as a NUMBER in the JSON body for deleteInstanceAccount, not appended to
        // the path — the path that used to carry it (/partner/deleteInstance/{token}/{id}) doesn't exist
        // at the provider, which is exactly why every call used to 404 and get misread as "already gone".
        if (!long.TryParse(instanceId, out var numericInstanceId))
            throw new GreenApiProvisioningException($"{safeLabel}: instance id \"{instanceId}\" is not numeric.");

        HttpStatusCode? statusCode;
        string? body;
        try
        {
            (body, statusCode) = await SendAsync(HttpMethod.Post, uri, safeLabel, ct, new { idInstance = numericInstanceId });
        }
        catch (GreenApiProvisioningException)
        {
            return new InstanceDeletion(Success: false);
        }

        // §30.4: an instance that no longer exists at the provider is treated as successfully deleted —
        // idempotency by interpreting the response, not by hoping the call never runs twice. B7: with the
        // path now correct, a 404 is a genuinely rare case — trust it ONLY when the body itself says the
        // instance is gone (rather than any 404, which is how the old, wrong path silently "succeeded" at
        // deleting nothing).
        var isSuccess = ReadBool(body, "isSuccess") ?? ReadBool(body, "result");
        if (statusCode == HttpStatusCode.NotFound)
            return new InstanceDeletion(Success: BodyIndicatesInstanceAlreadyGone(body));

        return new InstanceDeletion(Success: isSuccess ?? statusCode is HttpStatusCode.OK);
    }

    /// <summary>B7: the ONLY thing that makes a 404 count as a successful deletion — a body that GREEN-API
    /// actually uses to say "no such instance", rather than treating any 404 (e.g. from a wrong path) as
    /// success. Checked against the error message property partner endpoints use for this.</summary>
    private static bool BodyIndicatesInstanceAlreadyGone(string? body)
    {
        var message = ReadString(body ?? string.Empty, "message") ?? ReadString(body ?? string.Empty, "error");
        return message is not null &&
               (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("instance not exists", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Sends one request, logging duration under the redacted <c>safeLabel</c> exactly like
    /// <see cref="GreenApiTransport"/> (§24.3/§28.1), and throws <see cref="GreenApiProvisioningException"/>
    /// — never the raw <see cref="HttpRequestException"/>/response — for anything other than a successful
    /// 2xx response.</summary>
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
            logger.LogInformation("GREEN-API request {SafeLabel} took {DurationMs}ms, status=(network failure)",
                safeLabel, stopwatch.ElapsedMilliseconds);
            throw new GreenApiProvisioningException($"{safeLabel}: network failure.", ex);
        }
        // N11: HttpClient.Timeout (§28.1/§24.3's GreenApi:TimeoutSeconds) fires as TaskCanceledException,
        // NOT HttpRequestException — the catch above never saw it, so a slow provider response propagated
        // as a raw TaskCanceledException out of every method in this class, including
        // GetPhoneNumberAsync/GetQrAsync, whose own doc comments promise "never throws, null on any
        // failure". The `!ct.IsCancellationRequested` guard is what tells a genuine internal timeout
        // apart from the CALLER's own token being cancelled (e.g. a caller that still legitimately wants
        // OperationCanceledException to propagate) — only the former is converted here.
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger.LogInformation("GREEN-API request {SafeLabel} took {DurationMs}ms, status=(timeout)",
                safeLabel, stopwatch.ElapsedMilliseconds);
            throw new GreenApiProvisioningException($"{safeLabel}: request timed out.", ex);
        }

        using (response)
        {
            stopwatch.Stop();
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogInformation("GREEN-API request {SafeLabel} took {DurationMs}ms, status={StatusCode}",
                safeLabel, stopwatch.ElapsedMilliseconds, (int)response.StatusCode);

            // 404 is a legitimate, meaningful response for DeleteInstance (§30.4) — let the caller decide
            // what it means instead of turning every non-2xx into the same exception.
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new GreenApiProvisioningException($"{safeLabel}: provider returned {(int)response.StatusCode}.");

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
