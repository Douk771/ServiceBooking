using Microsoft.Extensions.Logging;

namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// Default sender, matching <c>Notifications:StaffPush:Provider = logging</c> (ARCHITECTURE_CYCLE9.md
/// §105.3) — no outbound network call, ever. In practice this class is never actually reached in normal
/// operation: when the provider is <c>logging</c>, <c>GET /api/push/config</c> reports
/// <c>enabled=false</c>, the frontend never subscribes anyone, and <c>StaffPushDispatchTask</c> has
/// nothing queued to send. It exists anyway (same reasoning as <c>LoggingNotificationTransport</c>) so
/// the DI graph is a fixed shape independent of the configured provider, and so a stray row that somehow
/// got queued while disabled still "sends" harmlessly to the log instead of a null-reference.
/// </summary>
public sealed class LoggingWebPushSender(ILogger<LoggingWebPushSender> logger) : IWebPushSender
{
    public Task<WebPushSendOutcome> SendAsync(
        WebPushSubscriptionTarget target, string payloadJson, int ttlSeconds, string topic, CancellationToken ct)
    {
        logger.LogInformation(
            "web-push (logging stub): would send to subscription {SubscriptionId}, ttl={TtlSeconds}s, topic={Topic}",
            target.SubscriptionId, ttlSeconds, topic);
        return Task.FromResult<WebPushSendOutcome>(new WebPushSendOutcome.Sent());
    }
}
