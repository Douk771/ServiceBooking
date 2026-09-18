namespace ServiceBooking.Core.Enums;

/// <summary>How a phone number ended up on <see cref="Entities.NotificationOptOut"/> (US-33).</summary>
public enum OptOutSource
{
    /// <summary>Followed the unsubscribe link in a message.</summary>
    Link,

    /// <summary>Toggled off from their own client cabinet.</summary>
    Cabinet,
}
