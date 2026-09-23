namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// Classification of one push-service response (ARCHITECTURE_CYCLE9.md §105.8's table — "таблица, а не
/// «если не 2xx, то повторим»"). A closed set of record subtypes, matching the same pattern
/// <c>SendOutcome</c> uses for WhatsApp/MAX sends (<c>Services/Notifications/INotificationTransport.cs</c>) —
/// <c>Services/Scheduling/Tasks/StaffPushDispatchTask.cs</c> switches over it exhaustively.
/// </summary>
public abstract record WebPushSendOutcome
{
    /// <summary>200/201/202 — accepted for delivery by the push service.</summary>
    public sealed record Sent : WebPushSendOutcome;

    /// <summary>404/410 Gone — the subscription itself is dead. Caller deletes the
    /// <c>PushSubscription</c> row in the same pass; no retry.</summary>
    public sealed record Gone : WebPushSendOutcome;

    /// <summary>401/403 — almost always a VAPID key-pair mismatch (R12). No retry.</summary>
    public sealed record AuthRejected(string? Detail) : WebPushSendOutcome;

    /// <summary>413 — encrypted payload too large. No retry (this is a code-side bug, not transient).</summary>
    public sealed record PayloadTooLarge(string? Detail) : WebPushSendOutcome;

    /// <summary>429, 5xx, timeout, or connection refused — retry per <c>WebPushOptions.DispatchOptions.MaxAttempts</c>.</summary>
    public sealed record Transient(string? Detail) : WebPushSendOutcome;
}
