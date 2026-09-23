namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>Everything the dispatch task needs to know about one device to send it a message —
/// decrypted just before this call, in the background task, never held longer than one send
/// (§105.2).</summary>
public sealed record WebPushSubscriptionTarget(Guid SubscriptionId, string Endpoint, string P256dh, string Auth);

/// <summary>
/// Sends one Web Push message to one device (ARCHITECTURE_CYCLE9.md §105.1, §105.8). Two
/// implementations, selected the same way <c>INotificationTransport</c> is (Program.cs switch on the
/// configured provider): <see cref="LoggingWebPushSender"/> (default, no network call, matches
/// <c>Notifications:StaffPush:Provider = logging</c>) and <see cref="LibWebPushSender"/> (real delivery
/// via the named <c>web-push</c> <see cref="System.Net.Http.HttpClient"/>, VAPID-authenticated). The one
/// caller is <c>Services/Scheduling/Tasks/StaffPushDispatchTask.cs</c>.
/// </summary>
public interface IWebPushSender
{
    /// <param name="target">The device to send to.</param>
    /// <param name="payloadJson">The (plaintext, not yet encrypted — the implementation handles RFC 8291
    /// encryption) push message body, already rendered server-side.</param>
    /// <param name="ttlSeconds"><c>TTL</c> header — <c>min(3600, seconds to visit start)</c> per §105.8.</param>
    /// <param name="topic"><c>Topic</c> header — <c>"b-{bookingId}"</c>, collapses repeat sends about the
    /// same visit at the push service.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<WebPushSendOutcome> SendAsync(
        WebPushSubscriptionTarget target, string payloadJson, int ttlSeconds, string topic, CancellationToken ct);
}
