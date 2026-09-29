using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Bookings;

/// <summary>
/// Cycle 22 D4 — the visit's total duration and its service names, read off a LOADED booking the one way
/// every caller already did it inline (US-67, ARCHITECTURE_CYCLE6.md §47.2): from the BookingServices
/// rows (in Position order) when there are any, else from the single legacy Booking.Service — which must
/// then be loaded. The fallback exists only for a row-less visit, which the AddBookingServices backfill
/// rules out; without it such a visit would sum to zero minutes and collapse to EndTime == StartTime.
/// </summary>
public static class BookingServiceExtensions
{
    public static int TotalDurationMinutes(this Booking booking) =>
        booking.BookingServices is { Count: > 0 }
            ? booking.BookingServices.Sum(bs => bs.DurationMinutes)
            : booking.Service.DurationMinutes;

    public static List<string> ServiceNames(this Booking booking) =>
        booking.BookingServices is { Count: > 0 }
            ? booking.BookingServices.OrderBy(bs => bs.Position).Select(bs => bs.NameSnapshot).ToList()
            : [booking.Service.Name];
}
