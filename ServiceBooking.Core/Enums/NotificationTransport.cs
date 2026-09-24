namespace ServiceBooking.Core.Enums;

/// <summary>
/// Which messaging network a <see cref="NotificationChannel"/> uses. Declared as an enum rather than
/// assumed implicitly so a second transport is a new member plus an adapter, not a schema migration
/// (ARCHITECTURE_CYCLE4.md §23.1). <see cref="Max"/> is that second transport (ARCHITECTURE_CYCLE9.md
/// §104.2, US-119) — GREEN-API's own separate MAX product, same URL/response shape as WhatsApp
/// (§104.9), a different provider account.
///
/// APPEND-ONLY: this enum participates in persisted data (<c>NotificationChannel.Transport</c>,
/// <c>ChannelCompanyAssignment.Transport</c>) and in a unique index — a reordering would silently
/// relabel every already-persisted row. A new member is added only at the end.
/// </summary>
public enum NotificationTransport
{
    WhatsApp = 0,
    Max = 1,
}
