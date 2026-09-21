namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Typed binding of the <c>Notifications</c> configuration section (ARCHITECTURE_CYCLE4.md §37). Field
/// names here and the <c>.env</c>/<c>docker-compose.prod.yml</c> variable names in §37 are the ones
/// devops wires up (T4-D2) — do not rename without updating both.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    // N10 (review round 2): the "Enabled" master switch that used to live here is gone. It never actually
    // gated anything downstream (NotificationGate/the scheduled tasks never read it — the real gate is
    // "is there a paid, connected channel"), and DeploymentSafetyChecks' fail-fast checks were fixed in
    // I4 to key off Provider instead. A config key nobody reads is worse than no key: it looks like a
    // switch and isn't one. Do not reintroduce it without wiring it to something real.

    /// <summary><c>"logging"</c> (default, safe everywhere) or <c>"green-api"</c> (real provider,
    /// requires a partner token and is refused outside Production by
    /// <c>DeploymentSafetyChecks.ValidateNotificationSecrets</c>).</summary>
    public string Provider { get; set; } = "logging";

    /// <summary>Base64-encoded 32-byte AES-GCM key protecting every channel's provider token (§24).</summary>
    public string? EncryptionKey { get; set; }

    /// <summary>One-shot rotation acknowledgement — the new key's 8-hex-character id (§24.4, §24.5).
    /// Empty in ordinary operation; must be removed from <c>.env</c> right after a rotation.</summary>
    public string? KeyRotationAck { get; set; }

    /// <summary>Where the key fingerprint (§24.5) is persisted, relative to the app's content root
    /// unless rooted.</summary>
    public string KeyFingerprintPath { get; set; } = "App_Data/state/.notifications-key-fingerprint";

    /// <summary>GREEN-API partner token — creates/deletes instances on the platform's own account. Must
    /// be empty outside Production (§24.2 p.3): a real value here could delete a live salon's instance
    /// from a dev/test run.</summary>
    public string? PartnerToken { get; set; }

    /// <summary>Shared secret compared against the <c>{token}</c> path segment of the provider webhook
    /// route (§32). Handed to the provider as <c>webhookUrlToken</c> — the one secret in this section
    /// that is expected to leave the server.</summary>
    public string? WebhookToken { get; set; }

    /// <summary>HMAC key for the unsubscribe link token (§31.4). Deliberately separate from
    /// <see cref="WebhookToken"/>, which leaves the server; this one never does.</summary>
    public string? UnsubscribeKey { get; set; }

    public GreenApiOptions GreenApi { get; set; } = new();
    public DispatchOptions Dispatch { get; set; } = new();

    /// <summary>Random spread (±minutes) applied to a reminder's due time so many reminders due at the
    /// same lead time do not all become due in the same instant (§34.1). Deterministic per row, from the
    /// row's id — see <c>NotificationTiming</c>.</summary>
    public int ReminderJitterMinutes { get; set; } = 15;

    /// <summary>A channel stuck in <c>Connecting</c> longer than this is abandoned by
    /// <c>ChannelHealthTask</c> (§29.3): its instance is deleted and it returns to <c>NotConnected</c>.</summary>
    public int UnauthorizedInstanceTimeoutMinutes { get; set; } = 15;

    /// <summary>Minimum gap between two "send a test message" requests on the same channel (US-55 p.7).</summary>
    public int TestMessageCooldownMinutes { get; set; } = 5;

    /// <summary>Consecutive send failures on a channel before it is marked <c>Disconnected</c> (§26.4).</summary>
    public int ConsecutiveFailureThreshold { get; set; } = 5;

    /// <summary>Sandbox mode (US-35 p.5): when non-empty, the real transport only actually sends to these
    /// canonical phone numbers and logs everything else as if it were the logging stub.</summary>
    public string[] AllowedRecipients { get; set; } = [];

    /// <summary>T-24 (ARCHITECTURE_CYCLE5.md §52.3) — raw string, parsed by
    /// <see cref="Core.Enums.ProviderDeliveryConsentMode"/> and validated at startup by
    /// <see cref="DeploymentSafetyChecks.ValidateProviderDeliveryConsentMode"/> (an unrecognized value
    /// fails loud, same convention as <see cref="Provider"/>). Default matches the customer's decision
    /// (§52.3.1) — "AccountsOnly" is not a placeholder, it is the value this cycle actually ships with.</summary>
    public string ProviderDeliveryConsent { get; set; } = "AccountsOnly";

    public sealed class GreenApiOptions
    {
        public string ApiUrl { get; set; } = "https://api.green-api.com";
        public int TimeoutSeconds { get; set; } = 15;

        /// <summary><c>"IPv4First"</c> (default, §28.1) or <c>"System"</c> — the emergency escape hatch
        /// that fully disables the custom <c>ConnectCallback</c> without a rebuild.</summary>
        public string ConnectPreference { get; set; } = "IPv4First";

        public int ConnectTimeoutSeconds { get; set; } = 5;
        public int PerAddressConnectTimeoutSeconds { get; set; } = 2;
    }

    public sealed class DispatchOptions
    {
        public int BatchSize { get; set; } = 200;
        public int BudgetSeconds { get; set; } = 50;
        public int MaxParallelChannels { get; set; } = 8;
        public int PauseMinMs { get; set; } = 5000;
        public int PauseMaxMs { get; set; } = 15000;
        public int InFlightGraceMinutes { get; set; } = 5;
        public int MaxAttempts { get; set; } = 5;
    }
}
