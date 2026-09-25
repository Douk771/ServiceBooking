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
        // case were silently ignored). The contract (contracts/cycle17/openapi.yaml) documents `status`
        // as an enum of exact names (upcoming | Pending | Confirmed | Cancelled | Completed | NoShow),
        // so numeric strings must be rejected up front: Enum.TryParse<T>(string, ...) has a well-known
        // gotcha where it happily parses ANY integer literal as a defined enum value — even ordinals
        // that don't correspond to any member (e.g. "99", "-1") — which silently produced a 200 with an
        // empty result instead of the documented 400. Guard against that whole class by refusing to
        // fall through to Enum.TryParse for purely-numeric input.
        if (status.Length > 0 && status.All(c => char.IsDigit(c) || c == '-'))
        {
            filter = default;
            return false;
        }

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
