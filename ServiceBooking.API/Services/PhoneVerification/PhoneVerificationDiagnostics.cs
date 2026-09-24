namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// In-memory, process-lifetime record of "is the webhook currently subscribed" (ARCHITECTURE_CYCLE12.md
/// §146.3, §152) — written by <c>MaxWebhookStartupSubscriber</c> and the <c>max-webhook-renew</c>
/// scheduled task, read by <c>GET /phone-verification/config</c>'s <c>healthy</c> field and
/// <c>GET /admin/phone-verification/diagnostics</c>. Deliberately NOT persisted: a restart re-subscribes
/// immediately (the startup subscriber), so there is nothing here worth surviving a restart for — and
/// keeping it out of the database means this can never become a second, slightly-stale source of truth
/// next to whatever the platform itself thinks the subscription state is.
///
/// Registered as a singleton (Program.cs) — a lock guards the two fields written together since the
/// startup subscriber and the periodic renewal task can, in principle, run concurrently.
/// </summary>
public sealed class PhoneVerificationDiagnostics
{
    private readonly object _gate = new();
    private bool _webhookSubscribed;
    private DateTime? _lastAttemptAtUtc;
    private string? _lastError;

    public bool WebhookSubscribed { get { lock (_gate) return _webhookSubscribed; } }
    public DateTime? LastSubscriptionAttemptAtUtc { get { lock (_gate) return _lastAttemptAtUtc; } }
    public string? LastSubscriptionError { get { lock (_gate) return _lastError; } }

    public void RecordSubscriptionAttempt(bool success, string? error)
    {
        lock (_gate)
        {
            _lastAttemptAtUtc = DateTime.UtcNow;
            _webhookSubscribed = success;
            _lastError = success ? null : error;
        }
    }
}
