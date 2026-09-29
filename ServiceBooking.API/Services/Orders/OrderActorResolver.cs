using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>Who performed the action that is about to be journaled (the name is a snapshot, staff only).</summary>
public sealed record OrderActor(OrderActorKind Kind, string? UserId, string? NameSnapshot);

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.6 — one resolution order for every journal writer (the BookingActorResolver pattern):
/// SuperAdmin → staff of THIS shop → a signed-in customer → a guest.
/// </summary>
public class OrderActorResolver(AppDbContext db)
{
    /// <summary>An action taken from the cabinet: SuperAdmin, otherwise staff (rights were checked by the caller).</summary>
    public async Task<OrderActor> ResolveStaffAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = userId is null ? null : await NameOfAsync(userId);
        var kind = principal.IsInRole("SuperAdmin") ? OrderActorKind.SuperAdmin : OrderActorKind.Staff;
        return new OrderActor(kind, userId, name);
    }

    /// <summary>The customer's own action: their account when the token is theirs, otherwise a guest.</summary>
    public async Task<OrderActor> ResolveCustomerAsync(ClaimsPrincipal principal, string? orderCustomerUserId, string? guestName)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null && userId == orderCustomerUserId)
            return new OrderActor(OrderActorKind.Customer, userId, await NameOfAsync(userId));
        return new OrderActor(OrderActorKind.Guest, null, guestName);
    }

    public static OrderActor ForCustomer(OrderActorKind kind, string? userId, string? name) => new(kind, userId, name);

    private async Task<string?> NameOfAsync(string userId)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName }).FirstOrDefaultAsync();
        return user is null ? null : $"{user.FirstName} {user.LastName}".Trim();
    }
}
