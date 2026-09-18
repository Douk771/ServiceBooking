using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// A WhatsApp channel: one GREEN-API instance = one number = one payment (ARCHITECTURE_CYCLE4.md §23.1,
/// decision "variant B"). Owned by a platform account (<see cref="OwnerUserId"/>, same key as
/// <see cref="AccountSubscription"/>); companies are assigned to it via <see cref="ChannelCompanyAssignment"/>.
/// </summary>
public class NotificationChannel
{
    public Guid Id { get; set; }

    public string OwnerUserId { get; set; } = string.Empty;
    public AppUser Owner { get; set; } = null!;

    public NotificationTransport Transport { get; set; } = NotificationTransport.WhatsApp;
    public ChannelState State { get; set; } = ChannelState.NotConnected;

    // Canonical (PhoneNormalizer), filled from `wid` after authorization (US-53 p.6).
    public string? PhoneNumber { get; set; }

    // GREEN-API's idInstance. Unique among non-null values (a deleted instance's id is not reused
    // outright, but the slot must be free for a fresh CreateInstanceAsync at the same channel row).
    public string? ProviderInstanceId { get; set; }

    // §24: never leaves the server. See SecretProtector for the ciphertext format.
    public string? ProviderSecretCiphertext { get; set; }
    public string? ProviderSecretKeyId { get; set; }

    // An instance that MUST be deleted at the provider but whose deletion hasn't been confirmed yet
    // (§30.4 "database first, then provider, retry from the database"). Non-null means
    // ChannelHealthTask must retry DeleteInstanceAsync on its next pass.
    public string? OrphanedInstanceId { get; set; }

    // Owner's request to buy the option (US-57 p.2). Non-null with PaidUntilUtc still null = "request
    // pending superadmin approval".
    public DateTime? RequestedAtUtc { get; set; }

    // Reserved, deliberately unused in cycle 4 (§23.1, US-57 p.2 / US-62 p.4: email collection and the
    // disruption email were both cut). Kept in the schema so a future reintroduction doesn't need a
    // migration; not exposed in any DTO.
    public string? ContactEmail { get; set; }

    public DateTime? PaidFromUtc { get; set; }
    public DateTime? PaidUntilUtc { get; set; }
    public bool IsSuspendedByAdmin { get; set; }

    // One field for all four idle causes (§30.3) — recomputed only by ChannelHealthTask.
    public DateTime? IdleSinceUtc { get; set; }
    public DateTime? IdleWarningSentAtUtc { get; set; }

    public DateTime? InstanceCreatedAtUtc { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }
    public DateTime? LastStateCheckAtUtc { get; set; }
    public int ConsecutiveSendFailures { get; set; }
    public DateTime? LastTestMessageAtUtc { get; set; }
    public DateTime? DisruptionNotifiedAtUtc { get; set; }

    public DateTime? RiskAcceptedAtUtc { get; set; }
    public string? RiskAcceptedVersion { get; set; }

    public Guid? ReplacedByChannelId { get; set; }
    public NotificationChannel? ReplacedByChannel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChannelCompanyAssignment> Assignments { get; set; } = [];
}
