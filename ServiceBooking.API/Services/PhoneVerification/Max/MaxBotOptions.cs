namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>Typed binding of <c>PhoneVerification:Max</c> — everything specific to talking to the MAX
/// bot platform (ARCHITECTURE_CYCLE14.md §150.1). Kept as a nested section, not flattened, so a future
/// second method (call/SMS) gets an equally-scoped sibling section instead of everyone's settings
/// interleaved under one flat namespace.</summary>
public sealed class MaxBotOptions
{
    /// <summary>Bot's own @username — the deep link is <c>https://max.ru/&lt;BotUsername&gt;?start=…</c>.
    /// Not a secret.</summary>
    public string? BotUsername { get; set; }

    /// <summary>Bot API token. Empty in git — only ever set via <c>PHONEVERIFY_MAX_BOT_TOKEN</c>. Also
    /// the HMAC key for the contact-signature check (§147.1) — the ONE place this token is used as a
    /// cryptographic key, not just a bearer credential.</summary>
    public string? BotToken { get; set; }

    // Проверено вживую 24.09.2026 при первом включении подсистемы в проде, и умолчание пришлось
    // сменить: `platform-api2.max.ru` и `platform-api.max.ru` резолвятся в ОДИН адрес
    // (155.212.204.149), но на platform-api2 не поднимается TLS — curl отдаёт 000, а наш
    // `subscribe` уходил в таймаут. На platform-api.max.ru тот же токен отвечает 200 на GET /me.
    // Архитектура (§150) называла platform-api2 по разведданным, до первого живого вызова —
    // это ровно тот случай, о котором предупреждал §9 V4. Хост переопределяется через
    // PHONEVERIFY_MAX_API_URL, если MAX снова переедет.
    public string ApiUrl { get; set; } = "https://platform-api.max.ru";

    /// <summary>Secret webhook path segment. Empty in git — only ever set via
    /// <c>PHONEVERIFY_MAX_WEBHOOK_TOKEN</c>. Never logged (masked both at the nginx layer and by
    /// <c>MaskSensitiveRequestPath</c> — same coordinated fix as R12/cycle 9's incident).</summary>
    public string? WebhookToken { get; set; }

    /// <summary>Public HTTPS origin this deployment is reachable at — what the webhook URL handed to
    /// <c>subscribe</c> is built from (e.g. <c>https://ezbook.ru</c>).</summary>
    public string? PublicBaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>О3 — platform-wide outgoing-call budget, enforced by <c>MaxBotClient</c>'s
    /// <c>TokenBucketRateLimiter</c>.</summary>
    public int GlobalRequestsPerSecond { get; set; } = 30;

    /// <summary>О3 — per-chat outgoing-message budget, enforced by <c>MaxBotClient</c>'s
    /// <c>PartitionedRateLimiter</c>.</summary>
    public int PerChatMessagesPerSecond { get; set; } = 2;

    /// <summary>§147.3 p.6 — cap on the size of an incoming <c>vcf_info</c> string; anything larger is
    /// treated as <c>NoPhoneInContact</c> rather than parsed.</summary>
    public int MaxVcardBytes { get; set; } = 16384;
}
