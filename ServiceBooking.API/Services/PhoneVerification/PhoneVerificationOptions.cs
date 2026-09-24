namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// Typed binding of the top-level <c>PhoneVerification</c> configuration section
/// (ARCHITECTURE_CYCLE14.md §150.1). Deliberately its OWN top-level section, not a subsection of
/// <c>Notifications</c> — a structural guarantee (§144.1, R5) that this platform subsystem never grows a
/// dependency on which channel/tariff/legal gate a salon happens to have.
/// </summary>
public sealed class PhoneVerificationOptions
{
    public const string SectionName = "PhoneVerification";

    /// <summary><c>"stub"</c> (default, ships with every deploy — SPEC §0.5's "невыпущенность") or
    /// <c>"max-bot"</c> (the one real adapter this cycle ships). Any other value fails startup — same
    /// append-only-provider convention as <c>NotificationOptions.Provider</c>.</summary>
    public string Provider { get; set; } = "stub";

    /// <summary>П7 — how long an unlinked/unconfirmed session stays valid.</summary>
    public int SessionTtlMinutes { get; set; } = 10;

    /// <summary>How long a Verified session may still be presented to <c>POST /api/auth/register</c>
    /// after the moment it was verified.</summary>
    public int VerifiedSessionUsableMinutes { get; set; } = 30;

    /// <summary>Q12/§148.4 — the poll interval <c>GET /phone-verification/config</c> hands the frontend.
    /// A recommendation, not an enforced limit (the poll endpoint carries no rate-limiting policy,
    /// §150.4) — kept as configuration rather than a wire constant only so a future tuning pass is a
    /// config edit.</summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Р5 — the ceiling on distinct OTHER numbers one MAX account may have verified.</summary>
    public int MaxPhonesPerExternalAccount { get; set; } = 3;

    /// <summary>Cheap anti-abuse: how many still-open (Pending/Linked) sessions one canonical phone may
    /// have at once before <c>POST /phone-verification/sessions</c> answers 429.</summary>
    public int MaxOpenSessionsPerPhone { get; set; } = 3;

    /// <summary>Base64-encoded 32-byte HMAC key for <c>ExternalAccountKey</c> (§142.3). Empty in
    /// git — only ever set via <c>PHONEVERIFY_EXTERNAL_KEY</c> on the deployment host. Its OWN key,
    /// never <c>Notifications:EncryptionKey</c> — see §142.3 for why reusing that key would be wrong.</summary>
    public string? ExternalKeyHmac { get; set; }

    public Max.MaxBotOptions Max { get; set; } = new();
}
