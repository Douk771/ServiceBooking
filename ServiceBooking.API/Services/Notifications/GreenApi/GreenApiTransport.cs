using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services;

namespace ServiceBooking.API.Services.Notifications.GreenApi;

/// <summary>
/// The real WhatsApp transport (<c>Notifications:Provider=green-api</c>, ARCHITECTURE_CYCLE4.md §28).
/// Stateless: every call takes a salon's <see cref="ChannelCredentials"/> explicitly, never reads them
/// from anywhere ambient. Nothing here ever throws out to the caller (§24.3 rung 3) — every failure mode
/// becomes a <see cref="SendOutcome"/>, so the dispatcher never has to catch a provider-specific exception
/// type, and a request URL (which carries the channel's token) can never reach a log via an unhandled
/// exception's message.
/// </summary>
public sealed class GreenApiTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<GreenApiTransport> logger) : INotificationTransport
{
    public async Task<SendOutcome> SendAsync(ChannelCredentials credentials, string canonicalPhone, string text, CancellationToken ct)
    {
        var opts = options.Value;

        // Sandbox (US-35 p.5): a non-empty allow-list restricts REAL sends to those numbers; everything
        // else behaves exactly like the logging stub, so a misconfigured pilot can never message a real
        // customer by accident.
        if (opts.AllowedRecipients.Length > 0 && !opts.AllowedRecipients.Contains(canonicalPhone))
        {
            logger.LogInformation(
                "GREEN-API sandbox: {MaskedPhone} is not in Notifications:AllowedRecipients — logging " +
                "instead of sending (length={Length})",
                LogMasking.Phone(canonicalPhone), text.Length);
            return new SendOutcome.Sent($"sandbox-{Guid.NewGuid():N}");
        }

        var client = httpClientFactory.CreateClient("green-api");
        var chatId = GreenApiUrls.BuildChatId(canonicalPhone);
        var (uri, safeLabel) = GreenApiUrls.SendMessage(opts.GreenApi.ApiUrl, credentials.InstanceId, credentials.Token);

        var stopwatch = Stopwatch.StartNew();
        var networkFailure = false;
        HttpStatusCode? statusCode = null;
        string? body = null;

        try
        {
            using var response = await client.PostAsJsonAsync(uri, new { chatId, message = text }, ct);
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            // DNS failure, connection refused, TLS failure, etc. — no response was ever received.
            networkFailure = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient's own Timeout (Notifications:GreenApi:TimeoutSeconds) elapsed — NOT the
            // caller's own cancellation. When ct.IsCancellationRequested IS true (the dispatcher's
            // per-pass budget ran out, §26.2/§26.1), this filter is false and the exception is left to
            // propagate — the dispatch loop's own OperationCanceledException handling is what must see
            // it, not this classifier, or a budget cutoff would misreport as "one message failed" instead
            // of "the pass ended early with N messages left".
            networkFailure = true;
        }
        finally
        {
            stopwatch.Stop();
            // §28.1: duration lives in the SAME line as the redacted URL — a regression back to ~5s
            // connect times is visible at a glance in the log without any special tooling.
            logger.LogInformation(
                "GREEN-API request {SafeLabel} took {DurationMs}ms, status={StatusCode}",
                safeLabel, stopwatch.ElapsedMilliseconds, statusCode is null ? "(none)" : ((int)statusCode).ToString());
        }

        return GreenApiResultClassifier.Classify(networkFailure, statusCode, body);
    }
}
