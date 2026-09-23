using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// Real Web Push delivery (ARCHITECTURE_CYCLE9.md §105.1, §105.2, §105.8) — <c>Lib.Net.Http.WebPush</c>
/// (chosen over hand-rolling RFC 8291/8188 crypto, per the architecture's package-choice table; the
/// project convention against a second homegrown crypto implementation, CURRENT_STATE.md §6, rules out
/// anything else). Uses the named <c>"web-push"</c> <see cref="HttpClient"/> — a SEPARATE client from
/// <c>"green-api"</c> (§105.1: "Один клиент на два очень разных назначения — источник взаимного влияния
/// таймаутов").
///
/// One <see cref="PushServiceClient"/> per call, built fresh from the injected <see cref="HttpClient"/> —
/// cheap (it's a thin wrapper), and avoids holding any mutable state across calls that run in parallel
/// (§105.8: up to <c>MaxParallelEndpoints</c> concurrent sends).
/// </summary>
public sealed class LibWebPushSender(
    IHttpClientFactory httpClientFactory, IOptions<WebPushOptions> options, ILogger<LibWebPushSender> logger)
    : IWebPushSender
{
    public async Task<WebPushSendOutcome> SendAsync(
        WebPushSubscriptionTarget target, string payloadJson, int ttlSeconds, string topic, CancellationToken ct)
    {
        var opts = options.Value;

        var subscription = new PushSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);

        var message = new PushMessage(payloadJson)
        {
            Topic = topic,
            TimeToLive = ttlSeconds,
            // §105.8: Urgency: high — "к вам записались" is a time-sensitive alert, not a topic update.
            Urgency = PushMessageUrgency.High,
        };

        var authentication = new VapidAuthentication(opts.VapidPublicKey ?? "", opts.VapidPrivateKey ?? "")
        {
            Subject = opts.VapidSubject ?? "",
        };

        // Not disposed — same convention as GreenApiTransport/GreenApiProvisioning's own
        // httpClientFactory.CreateClient(...) call sites: the factory owns and pools the underlying
        // handler, this HttpClient wrapper is cheap and safe to let the GC collect.
        var httpClient = httpClientFactory.CreateClient("web-push");
        var client = new PushServiceClient(httpClient);

        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, authentication, ct);
            return new WebPushSendOutcome.Sent();
        }
        catch (PushServiceClientException ex)
        {
            var outcome = WebPushResponseClassifier.Classify(ex.StatusCode, ex.Body);
            if (outcome is WebPushSendOutcome.AuthRejected)
                logger.LogWarning(
                    "web-push: push service rejected the request with {StatusCode} — likely a VAPID key mismatch (R12).",
                    ex.StatusCode);
            return outcome;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // Connection refused / timeout / DNS failure — the push service is unreachable, not
            // rejecting. §105.8: temporary, retried like 5xx.
            return new WebPushSendOutcome.Transient(ex.Message);
        }
    }
}
