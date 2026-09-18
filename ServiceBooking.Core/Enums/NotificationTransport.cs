namespace ServiceBooking.Core.Enums;

/// <summary>
/// Which messaging network a <see cref="NotificationChannel"/> uses. One member today (WhatsApp) —
/// declared as an enum rather than assumed implicitly so a second transport later is a new member plus
/// an adapter, not a schema migration (ARCHITECTURE_CYCLE4.md §23.1).
/// </summary>
public enum NotificationTransport
{
    WhatsApp,
}
