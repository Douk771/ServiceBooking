using System.Net;

namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// Pure, DI-free classification of a push service's HTTP status code into
/// <see cref="WebPushSendOutcome"/> (ARCHITECTURE_CYCLE9.md §105.8's table) — pulled out of
/// <see cref="LibWebPushSender"/> specifically so <c>ServiceBooking.UnitTests</c> can exercise the whole
/// status-code table directly, the same "pure static method over plain data" shape
/// <c>DeploymentSafetyChecks</c>/<c>ChannelStateMapper</c> already use for this reason.
/// </summary>
public static class WebPushResponseClassifier
{
    public static WebPushSendOutcome Classify(HttpStatusCode statusCode, string? body) => (int)statusCode switch
    {
        404 or 410 => new WebPushSendOutcome.Gone(),
        401 or 403 => new WebPushSendOutcome.AuthRejected(body),
        413 => new WebPushSendOutcome.PayloadTooLarge(body),
        429 => new WebPushSendOutcome.Transient(body),
        // N7: 408 Request Timeout and 499 Client Closed Request (nginx's own convention for "client gave
        // up") are both about the REQUEST timing out or being abandoned, not about auth/payload/existence
        // of the subscription — retrying is exactly as appropriate here as it is for a 5xx/connection
        // failure, so both are Transient rather than falling into the auth-shaped default below.
        408 or 499 => new WebPushSendOutcome.Transient(body),
        _ when (int)statusCode >= 500 => new WebPushSendOutcome.Transient(body),
        // Anything else unexpected (a push service returning some other 4xx) is treated as
        // terminal-but-non-retryable auth-shaped rejection rather than silently retried forever.
        _ => new WebPushSendOutcome.AuthRejected(body),
    };
}
