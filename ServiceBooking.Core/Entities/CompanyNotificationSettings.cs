using ServiceBooking.Core.Enums;

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

    // ARCHITECTURE_CYCLE9.md §104.5 (US-125). Both default to 0 — PriorityChannel/WhatsApp — so every
    // existing company (and every company with no row here at all, per this entity's own "no row = every
    // default" convention) keeps today's exact single-transport behavior with no backfill (П12).
    public NotificationDeliveryMode DeliveryMode { get; set; } = NotificationDeliveryMode.PriorityChannel;
    public NotificationTransport PriorityTransport { get; set; } = NotificationTransport.WhatsApp;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }

    // ARCHITECTURE_CYCLE9.md §105.4/§105.7 (US-117, Q4). Deliberately its OWN column, not a bit in
    // EnabledTypeMask: PUT /api/companies/{id}/notification-settings answers 402 to every owner today
    // (AllowNotificationChannel = false on every tariff), so a free, tariff-free feature (SPEC П6) would
    // be unreachable if it lived in that same gated request/response shape. Default true — including
    // companies created before this cycle and companies with no CompanyNotificationSettings row at all
    // (this entity's own "no row = every default" convention, §104.4) — so no backfill is needed.
    // Turning this off HOLDS delivery (checked again at send time by StaffPushDispatchTask, §105.6) but
    // never deletes a master's subscription rows.
    public bool StaffPushEnabled { get; set; } = true;
}
