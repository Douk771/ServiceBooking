using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Bookings;

/// <summary>
/// The ONE place in the codebase that asks "does this phone number have any guest booking on it"
/// (ARCHITECTURE_CYCLE14.md §142.5, Q16) — backing <c>POST /api/profile/change-phone</c>'s gate
/// (US-14-17). Backed by the partial index <c>IX_Bookings_GuestPhone</c> (guest rows only —
/// registered-client bookings always have <c>GuestPhone == null</c>).
/// </summary>
public sealed class GuestBookingLookup(AppDbContext db)
{
    public Task<bool> HasGuestBookingsAsync(string canonicalPhone, CancellationToken ct) =>
        db.Bookings.AnyAsync(b => b.ClientId == null && b.GuestPhone == canonicalPhone, ct);
}
