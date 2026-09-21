namespace ServiceBooking.Core.Enums;

/// <summary>
/// <see cref="Entities.OutboundNotification"/>'s lifecycle — exactly seven members (US-28 p.2,
/// ARCHITECTURE_CYCLE4.md §23.4).
///
/// <b>The int value of <see cref="Pending"/> MUST stay 0.</b> The dispatcher's one and only index,
/// <c>IX_OutboundNotifications_Dispatch</c>, is a partial index defined with a raw SQL filter
/// (<c>.HasFilter("\"Status\" = 0")</c>) rather than a filter expressed against the enum, because EF's
/// filter-expression translation for enum comparisons is not reliably stable across providers/versions —
/// the raw literal is the one thing guaranteed to match what PostgreSQL actually stores (enums are
/// persisted as int, not as their name). Reordering these members — inserting a new status before
/// <see cref="Pending"/>, or reordering existing ones — silently breaks that filter: the index keeps
/// existing and keeps looking correct, but it stops matching the rows the dispatcher's query actually
/// selects, and the query silently falls back to a full scan. Add new members at the END only.
/// </summary>
public enum NotificationStatus
{
    Pending = 0,
    Sent,
    Delivered,
    Failed,
    Expired,
    Skipped,
    Cancelled,
}
