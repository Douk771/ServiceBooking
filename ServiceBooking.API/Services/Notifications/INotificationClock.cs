namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// The only source of "now" the dispatcher and the health task consult (ARCHITECTURE_CYCLE4.md §21 p.7).
/// Without this abstraction, neither the 5-15s pause between sends nor a multi-day idle period could be
/// tested except by actually waiting (SPEC §12 p.9) — <c>ServiceBooking.Tests</c>'s
/// <c>NotificationDispatchTestFactory</c> substitutes a fake that a test moves by hand.
/// </summary>
public interface INotificationClock
{
    DateTime UtcNow { get; }
}

/// <summary>Production implementation — the default registration everywhere outside the dedicated
/// dispatch-test host.</summary>
public sealed class SystemNotificationClock : INotificationClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
