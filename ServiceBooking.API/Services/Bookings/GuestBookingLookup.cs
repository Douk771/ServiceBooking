using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Bookings;

/// <summary>
/// The ONE place in the codebase that asks "does this phone number have any guest booking on it"
/// (ARCHITECTURE_CYCLE14.md §142.5, Q16) — backing <c>POST /api/profile/change-phone</c>'s gate
/// (US-14-17). Backed by the partial index <c>IX_Bookings_GuestPhone</c> (guest rows only —
/// registered-client bookings always have <c>GuestPhone</c> equal to null).  // SUBJECT-PHONE-GATE: not-account-scoped — doc comment, not executable code
/// Showcase bookings are not counted (ARCHITECTURE_CYCLE28.md §574.5): a fictional visit on a real number must not block a real
/// person's phone change.
/// </summary>
public sealed class GuestBookingLookup(AppDbContext db)
{
    public Task<bool> HasGuestBookingsAsync(string canonicalPhone, CancellationToken ct) =>
        db.Bookings.AnyAsync(b => b.ClientId == null && b.GuestPhone == canonicalPhone && b.ShowcaseKind == ShowcaseBookingKind.None, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — existence-only check used by ChangePhone to warn before claiming a number with guest bookings already on it; returns no guest content, pre-existing feature outside TD-03's five sewing points (ARCHITECTURE_CYCLE16.md §245.3)
}
