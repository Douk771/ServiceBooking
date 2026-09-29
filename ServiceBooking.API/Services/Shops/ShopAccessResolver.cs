using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>Either the error result the caller returns as is (404 / 403), or the shop and the caller's role in it.</summary>
public sealed record ShopAccessResult(ActionResult? Error, Company? Shop = null, ShopRole Role = ShopRole.Staff)
{
    public bool Ok => Error is null;
}

/// <summary>
/// ARCHITECTURE_CYCLE23.md §392.2, API_CONTRACT_CYCLE23.md §406.4 — the order of checks of every cabinet route of a shop:
/// (token, done by the framework) → the company exists AND is a shop (else 404: an id of a salon addresses nothing in the shop
/// API, §389.1) → the caller's role and the right table (<see cref="ShopAccess"/>; a non-member or a staff member on an owner
/// route gets 403 with an empty body). The membership half is <see cref="CompanyMembership"/> (the project's single SQL
/// definition of "works in this company"); a removed member loses access at once — it is checked on every request.
/// </summary>
public class ShopAccessResolver(AppDbContext db)
{
    public async Task<ShopAccessResult> ResolveAsync(
        Guid shopId, ClaimsPrincipal user, ShopPermission permission, bool asNoTracking = false, CancellationToken ct = default)
    {
        var query = asNoTracking ? db.Companies.AsNoTracking() : db.Companies;
        var shop = await query.FirstOrDefaultAsync(c => c.Id == shopId && c.Kind == CompanyKind.Orders, ct);
        if (shop is null) return new ShopAccessResult(new NotFoundResult());

        var role = await RoleOfAsync(shop.Id, user, ct);
        if (role is null || !ShopAccess.Allows(role.Value, permission))
            return new ShopAccessResult(new ForbidResult());
        return new ShopAccessResult(null, shop, role.Value);
    }

    /// <summary>SuperAdmin → SuperAdmin; a CompanyOwner member → Owner; a Master member → Staff; anyone else → null.</summary>
    public async Task<ShopRole?> RoleOfAsync(Guid shopId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return null;
        if (user.IsInRole("SuperAdmin")) return ShopRole.SuperAdmin;

        var memberRole = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.CompanyId == shopId && cm.UserId == userId)
            .Select(cm => (UserRole?)cm.Role).FirstOrDefaultAsync(ct);
        return memberRole switch
        {
            UserRole.CompanyOwner => ShopRole.Owner,
            UserRole.Master => ShopRole.Staff,
            _ => null
        };
    }
}
