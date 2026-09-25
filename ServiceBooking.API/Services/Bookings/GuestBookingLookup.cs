using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Bookings;

/// <summary>
/// The ONE place in the codebase that asks "does this phone number have any guest booking on it"
/// (ARCHITECTURE_CYCLE14.md §142.5, Q16) — backing <c>POST /api/profile/change-phone</c>'s gate
/// (US-14-17). Backed by the partial index <c>IX_Bookings_GuestPhone</c> (guest rows only —
/// registered-client bookings always have <c>GuestPhone</c> equal to null).  // SUBJECT-PHONE-GATE: not-account-scoped — doc comment, not executable code
/// </summary>
public sealed class GuestBookingLookup(AppDbContext db)
{
    public Task<bool> HasGuestBookingsAsync(string canonicalPhone, CancellationToken ct) =>
        db.Bookings.AnyAsync(b => b.ClientId == null && b.GuestPhone == canonicalPhone, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — existence-only check used by ChangePhone to warn before claiming a number with guest bookings already on it; returns no guest content, pre-existing feature outside TD-03's five sewing points (ARCHITECTURE_CYCLE16.md §245.3)
}
