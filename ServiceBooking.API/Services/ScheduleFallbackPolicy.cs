namespace ServiceBooking.API.Services;

/// <summary>
/// The one trust table for how a slot/availability request may fall back when the master has no
/// schedule row (ARCHITECTURE_CYCLE6.md §46.3, ARCHITECTURE_CYCLE10.md §103.1; cycle 22 D5 — formerly
/// inlined in both GetSlots and GetAvailability): manual + staff → <see cref="ScheduleFallback.DefaultWindow"/>;
/// manual + extendedHours + staff → <see cref="ScheduleFallback.WholeDay"/>; anything else (including
/// extendedHours without manual, or a non-staff caller asking for manual) → <see cref="ScheduleFallback.None"/>.
/// </summary>
public static class ScheduleFallbackPolicy
{
    public static ScheduleFallback For(bool manual, bool extendedHours, bool isStaff) =>
        manual && isStaff
            ? (extendedHours ? ScheduleFallback.WholeDay : ScheduleFallback.DefaultWindow)
            : ScheduleFallback.None;
}
