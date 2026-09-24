namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// Typed binding of the <c>Notifications:StaffPush</c> configuration section (ARCHITECTURE_CYCLE9.md
/// §105.2, §105.3, §105.4, §105.8). Deliberately a SIBLING of <see cref="NotificationOptions"/>, not a
/// nested class on it — the architecture doc names it as its own type ("<c>WebPushOptions</c>", C2) and
/// the whole subsystem it configures (Web Push to staff devices) is independent of the
/// WhatsApp/MAX notification pipeline that owns <see cref="NotificationOptions"/>: different secret
/// (VAPID, not the channel master key), different provider switch, different scheduled task.
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "Notifications:StaffPush";

    /// <summary><c>"logging"</c> (default, safe everywhere — SPEC П13's "невыпущенность") or
    /// <c>"web-push"</c> (real VAPID-authenticated delivery via <c>Lib.Net.Http.WebPush</c>). An
    /// unrecognized value fails startup, same convention as <see cref="NotificationOptions.Provider"/>
    /// (<see cref="DeploymentSafetyChecks.ValidateStaffPushSecrets"/>).</summary>
    public string Provider { get; set; } = "logging";

    /// <summary>VAPID private key (base64url, P-256). Empty in <c>appsettings.json</c> — only ever set
    /// via <c>WEBPUSH_VAPID_PRIVATE_KEY</c> on the deployment host (§105.2's "по образцу
    /// NOTIFICATIONS_ENCRYPTION_KEY, без изобретений"). REQUIRED, and must parse as a valid P-256 key,
    /// when <see cref="Provider"/> is <c>"web-push"</c> outside a developer environment.</summary>
    public string? VapidPrivateKey { get; set; }

    /// <summary>VAPID public key (base64url, P-256) — NOT a secret; its whole purpose is to be handed to
    /// the browser via <c>GET /api/push/config</c>'s <c>publicKey</c> field, the ONLY way it reaches the
    /// client bundle (no second <c>VITE_*</c> variable is ever introduced, §105.2).</summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>VAPID JWT <c>sub</c> claim — a <c>mailto:</c> or <c>https:</c> URL identifying the
    /// platform to the push service, per RFC 8292.</summary>
    public string? VapidSubject { get; set; }

    /// <summary>§105.4: at the 11th subscription for one user, the oldest (by <c>CreatedAtUtc</c>) is
    /// evicted silently — never surfaced to the caller as an error.</summary>
    public int MaxSubscriptionsPerUser { get; set; } = 10;

    /// <summary>§105.1: the named <c>"web-push"</c> <see cref="System.Net.Http.HttpClient"/>'s own
    /// timeout — deliberately a SEPARATE value from <c>NotificationOptions.GreenApiOptions.TimeoutSeconds</c>
    /// (one client per external dependency, so one dependency's slowness can never eat into the other's
    /// budget).</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    public DispatchOptions Dispatch { get; set; } = new();

    public sealed class DispatchOptions
    {
        /// <summary>§105.8: rows selected per pass, matching <c>IX_StaffPushNotifications_Dispatch</c>.</summary>
        public int BatchSize { get; set; } = 200;

        /// <summary>§105.8: the whole pass ends itself after this many seconds, same "budget, not a hard
        /// cutoff mid-send" shape as <see cref="NotificationOptions.DispatchOptions.BudgetSeconds"/>.</summary>
        public int BudgetSeconds { get; set; } = 20;

        /// <summary>§105.8: bounded PARALLEL sends across devices — unlike the WhatsApp/MAX dispatcher,
        /// there is no antiban pause between sends (push services impose no per-account rate limit at
        /// this volume), so this is the only throttle.</summary>
        public int MaxParallelEndpoints { get; set; } = 8;

        /// <summary>§105.8: temporary failures (429/5xx/timeout/connection refused) retry up to this many
        /// attempts before becoming <c>Failed</c>/<c>RetriesExhausted</c>.</summary>
        public int MaxAttempts { get; set; } = 4;

        /// <summary>N6: same "in-flight marker" convention <c>StaffPushNotification.LastAttemptAtUtc</c>'s
        /// own doc comment claims ("same convention as <c>OutboundNotification</c>") — a row whose
        /// <c>LastAttemptAtUtc</c> is within this many minutes of now is excluded from the candidate
        /// selection, exactly like <see cref="NotificationOptions.DispatchOptions.InFlightGraceMinutes"/>
        /// does for <c>OutboundNotification</c>. Without this, two overlapping dispatch passes (a slow
        /// pass still running when the next scheduled one starts) could select and send the same row
        /// twice.</summary>
        public int InFlightGraceMinutes { get; set; } = 5;
    }
}
