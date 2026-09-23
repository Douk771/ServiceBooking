using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Bookings;

public readonly record struct BookingActor(
    BookingActorKind Kind, string? UserId, string? NameSnapshot, UserRole? RoleSnapshot);

/// <summary>
/// ARCHITECTURE_CYCLE10.md §105/R7: who performed the action that is about to be journaled. One
/// resolution order for every one of the six call sites, checked strictly in this priority —
/// SuperAdmin -> staff of THIS company -> authenticated client -> guest — so, e.g., a SuperAdmin who
/// also happens to be a member of the company is never mis-attributed as "mastered" it.
/// </summary>
public class BookingActorResolver(AppDbContext db)
{
    public async Task<BookingActor> ResolveAsync(ClaimsPrincipal principal, Booking booking)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is not null && principal.IsInRole("SuperAdmin"))
        {
            var admin = await db.Users.FindAsync(userId);
            return new BookingActor(BookingActorKind.SuperAdmin, userId, NameOf(admin), null);
        }

        if (userId is not null)
        {
            var membership = await db.CompanyMembers.AsNoTracking().FirstOrDefaultAsync(cm =>
                cm.CompanyId == booking.CompanyId && cm.UserId == userId &&
                (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner));
            if (membership is not null)
            {
                var staffUser = await db.Users.FindAsync(userId);
                return new BookingActor(BookingActorKind.Staff, userId, NameOf(staffUser), membership.Role);
            }
        }

        if (userId is not null)
        {
            var client = await db.Users.FindAsync(userId);
            return new BookingActor(BookingActorKind.Client, userId, NameOf(client), null);
        }

        // Unauthenticated caller: the only path that can reach here is a guest creating their own
        // booking (every other write endpoint requires [Authorize]) — the guest has no account, so the
        // name snapshot is what they typed into the booking form itself.
        return new BookingActor(BookingActorKind.Guest, null, booking.GuestName, null);
    }

    private static string? NameOf(AppUser? user) => user is null ? null : $"{user.FirstName} {user.LastName}";
}
