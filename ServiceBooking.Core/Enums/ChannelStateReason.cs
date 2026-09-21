namespace ServiceBooking.Core.Enums;

/// <summary>
/// Machine-readable reason for a <see cref="ChannelState"/> transition (ARCHITECTURE_CYCLE4.md §23.1,
/// US-55 p.4). The Russian text shown to the owner is assembled server-side from this code (§23.1), so
/// there is exactly one place — not one per caller — where that wording lives.
/// </summary>
public enum ChannelStateReason
{
    /// <summary>Owner completed the QR authorization flow.</summary>
    Authorized,

    /// <summary>Provider reports the instance logged out / unauthorized (e.g. unlinked from the phone).</summary>
    ProviderReportsUnauthorized,

    /// <summary>Provider reports the instance blocked (banned) by WhatsApp.</summary>
    ProviderReportsBlocked,

    /// <summary><see cref="NotificationOptions"/> consecutive-send-failure threshold reached (§26.4).</summary>
    ConsecutiveSendFailuresExceeded,

    /// <summary>Owner explicitly disconnected the channel (US-56 p.1).</summary>
    DisconnectedByOwner,

    /// <summary>Superadmin suspended the channel/company (US-57 p.1).</summary>
    SuspendedByAdmin,

    /// <summary><see cref="ChannelHealthTask"/> abandoned an unauthorized instance stuck in
    /// <see cref="ChannelState.Connecting"/> past the timeout (§29.3).</summary>
    UnauthorizedInstanceTimedOut,

    /// <summary>Channel sat idle (no active paid company) past the configured number of days and its
    /// instance was deleted (§30.3, §30.4).</summary>
    IdleInstanceDeleted,

    /// <summary>Number banned; owner attached a replacement number in the same paid period (US-63).</summary>
    ReplacedAfterBan,

    /// <summary>I1: the channel's stored secret could no longer be decrypted (encryption key rotated or
    /// lost, ciphertext corrupted) — distinct from <see cref="ProviderReportsUnauthorized"/>, which means
    /// the OWNER's own action (or WhatsApp) logged the number out. This is a platform-side incident, not
    /// something the owner did, and needs its own wording (§24.5, §26.4) and its own recovery path: the
    /// instance is decommissioned outright (nothing usable was ever readable again) so Connect reopens.</summary>
    SecretUnavailable,

    /// <summary>T5-B13 (ARCHITECTURE_CYCLE5.md §52.2, ч. 5 ст. 18 152-ФЗ): the provider's response to
    /// instance creation reported a server country/region that does not match
    /// <c>Notifications:GreenApi:ServerCountry</c> — the instance is never wired up for use. A platform-
    /// side misconfiguration, not something the owner did or can retry their way out of; appended at the
    /// end, this enum is append-only (see <see cref="SecretUnavailable"/>'s own note).</summary>
    ServerCountryMismatch,
}
