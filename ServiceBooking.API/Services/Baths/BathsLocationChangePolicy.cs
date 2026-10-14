namespace ServiceBooking.API.Services.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.3.4 (I-2): a «Бани» company with future bookings may not change its city or time zone — the booked sessions are stored as local
/// date + start minute and shown to the guest in the company zone, so a shift would silently move the already accepted sessions.
/// </summary>
public static class BathsLocationChangePolicy
{
    public const string LockedText =
        "Нельзя сменить город или часовой пояс, пока есть будущие брони — сдвинулось бы время уже принятых сеансов. " +
        "Дождитесь окончания сеансов или отмените брони, затем смените город.";

    public static bool IsAllowed(bool cityChanged, string currentZoneId, string newZoneId, bool hasFutureBookings) =>
        !hasFutureBookings || (!cityChanged && string.Equals(currentZoneId, newZoneId, StringComparison.Ordinal));
}
