using System.Linq.Expressions;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services;

public enum ClientStatusFilterKind { All, Upcoming, ByStatus }

public readonly record struct ClientStatusFilter(ClientStatusFilterKind Kind, BookingStatus Status);

/// <summary>
/// Pure parsing/predicate logic for GET /api/bookings/client?status= (US-07), so the exact same rule
/// can be unit-tested (compiled and run in memory) and used by the controller (translated to SQL by EF).
/// </summary>
public static class BookingFilters
{
    /// <summary>Pure parser for GET /api/bookings/client?status=. False = unknown value → 400.</summary>
    public static bool TryParseClientStatus(string? status, out ClientStatusFilter filter)
    {
        if (string.IsNullOrEmpty(status))
        {
            filter = new ClientStatusFilter(ClientStatusFilterKind.All, default);
            return true;
        }

        if (string.Equals(status, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            filter = new ClientStatusFilter(ClientStatusFilterKind.Upcoming, default);
            return true;
        }

        // Case-insensitive on purpose (closes the US-07 defect where `cancelled`/`completed` in lower
        // case were silently ignored). Numeric enum values ("3") keep working exactly as before — that
        // was an explicit backward-compatibility guarantee of this method and callers may rely on it.
        //
        // Cycle 17 briefly added a guard here that refused all-numeric input, to stop Enum.TryParse's
        // known gotcha of parsing ANY integer literal ("99", "-1") into an undefined enum value. The
        // guard was removed: the `Enum.IsDefined` check below ALREADY rejects exactly those ordinals,
        // so the guard's only real effect was breaking the documented numeric values. It also missed
        // " 3 " (TryParse trims, All(IsDigit) does not), so it did not even close its own class.
        if (Enum.TryParse<BookingStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            filter = new ClientStatusFilter(ClientStatusFilterKind.ByStatus, parsed);
            return true;
        }

        filter = default;
        return false;
    }

    /// <summary>
    /// The "upcoming" rule as an expression so EF Core and the unit test share the exact same code
    /// (compile it in tests, translate it to SQL in the controller) — no duplicated predicate to drift.
    /// </summary>
    public static Expression<Func<Booking, bool>> Upcoming(DateOnly today, TimeOnly nowTime) =>
        b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending)
             && (b.Date > today || (b.Date == today && b.StartTime > nowTime));
}
