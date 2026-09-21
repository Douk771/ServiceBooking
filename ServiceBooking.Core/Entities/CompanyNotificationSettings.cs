namespace ServiceBooking.Core.Entities;

/// <summary>
/// Notification preferences belong to the COMPANY, not the channel (ARCHITECTURE_CYCLE4.md §23.3 —
/// several channels are planned for the future and must not force re-entering these settings). PK is
/// <see cref="CompanyId"/> itself; a missing row means "use the defaults on each property", so existing
/// companies never need a backfill.
/// </summary>
public class CompanyNotificationSettings
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // Bitmask over NotificationType — all types enabled by default so a fifth type later doesn't need a
    // schema migration (US-29 p.7): existing rows already have every currently-known bit set, and a new
    // bit is simply read as "on" for them too as long as the default constant is updated alongside the
    // new enum member.
    public int EnabledTypeMask { get; set; } = DefaultEnabledTypeMask;

    public const int DefaultEnabledTypeMask = ~0; // every bit set

    public int ReminderLeadMinutes { get; set; } = 1440; // 60..4320
    public int MinLeadMinutes { get; set; } = 120; // 0..720

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
