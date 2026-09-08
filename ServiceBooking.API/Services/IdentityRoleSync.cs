using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

/// <summary>
/// The single SQL-and-Identity truth for "which of the two membership-derived roles does this user
/// currently hold" (US-46, ARCHITECTURE.md §8). Recomputes Master/CompanyOwner purely from this user's
/// CompanyMember rows and reconciles ASP.NET Identity's role table to match. Never touches Client (the
/// base role every account gets at registration, AuthController) or SuperAdmin (owned solely by
/// PUT /api/admin/users/{id}/roles, AdminController.UpdateRoles) — those two are out of scope by design
/// (US-46 pp. 3, 4), so their current value is never read and never written here.
///
/// Call inside the SAME transaction as the membership change that triggered it, AFTER SaveChangesAsync
/// has persisted that change — UserManager writes to AspNetUserRoles through the same AppDbContext and
/// calls SaveChangesAsync internally, so the required order is:
///   change CompanyMember rows → db.SaveChangesAsync() → IdentityRoleSync.SyncAsync() → commit.
/// Reversing the first two steps would let SyncAsync compute a "desired" set that doesn't yet reflect
/// the membership change still sitting unsaved in the change tracker.
/// </summary>
public static class IdentityRoleSync
{
    private static readonly string[] ManagedRoles = [nameof(UserRole.Master), nameof(UserRole.CompanyOwner)];

    public static async Task SyncAsync(AppDbContext db, UserManager<AppUser> users, string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return; // deleted mid-transaction (account deletion, §7.4) — nothing to sync

        // One query: every distinct role this user's membership rows entitle them to, restricted to the
        // two roles this function owns. A Client-role CompanyMember row (a company's own customer, see
        // MastersController's comment on the same distinction) never contributes anything here.
        var desired = await db.CompanyMembers
            .Where(cm => cm.UserId == userId &&
                (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner))
            .Select(cm => cm.Role)
            .Distinct()
            .ToListAsync();
        var desiredNames = desired.Select(r => r.ToString()).ToHashSet();

        var current = await users.GetRolesAsync(user);
        var currentManaged = current.Where(r => ManagedRoles.Contains(r)).ToHashSet();

        var toAdd = desiredNames.Except(currentManaged).ToList();
        var toRemove = currentManaged.Except(desiredNames).ToList();

        if (toAdd.Count > 0)
            await users.AddToRolesAsync(user, toAdd);
        if (toRemove.Count > 0)
            await users.RemoveFromRolesAsync(user, toRemove);
    }
}
