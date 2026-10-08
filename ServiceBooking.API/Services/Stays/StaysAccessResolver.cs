using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record StaysAccessResult(ActionResult? Error, Company? Company = null, StaysMyRole Role = StaysMyRole.Housekeeper)
{
    public bool Ok => Error is null;
}

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.9 — "token → the company exists and is a «Дома» company AND the caller belongs to it (404, indistinguishable) →
/// the permission (403, empty body)". Membership is read from the database on every request, so a removed member loses access at once.
/// </summary>
public class StaysAccessResolver(AppDbContext db)
{
    public async Task<StaysAccessResult> ResolveAsync(
        Guid companyId, ClaimsPrincipal user, StaysPermission permission, bool asNoTracking = false, CancellationToken ct = default) =>
        await ResolveAnyAsync(companyId, user, [permission], asNoTracking, ct);

    /// <summary>Passes when the caller has AT LEAST ONE of the permissions (routes open to two rights, e.g. ViewCabinet or ViewSchedule).</summary>
    public async Task<StaysAccessResult> ResolveAnyAsync(
        Guid companyId, ClaimsPrincipal user, IReadOnlyCollection<StaysPermission> anyOf, bool asNoTracking = false, CancellationToken ct = default)
    {
        var query = asNoTracking ? db.Companies.AsNoTracking() : db.Companies;
        var company = await query.FirstOrDefaultAsync(c => c.Id == companyId && c.Kind == CompanyKind.Stays, ct);
        if (company is null) return new StaysAccessResult(new NotFoundResult());

        var role = await RoleOfAsync(company.Id, user, ct);
        if (role is null) return new StaysAccessResult(new NotFoundResult());
        if (!anyOf.Any(p => StaysAccess.Has(role.Value, p))) return new StaysAccessResult(new ForbidResult());
        return new StaysAccessResult(null, company, role.Value);
    }

    public async Task<StaysMyRole?> RoleOfAsync(Guid companyId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return null;
        if (user.IsInRole("SuperAdmin")) return StaysMyRole.SuperAdmin;

        var member = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.CompanyId == companyId && cm.UserId == userId)
            .Select(cm => new { cm.Role, cm.StaffPosition }).FirstOrDefaultAsync(ct);
        return member is null ? null : StaysAccess.RoleOfMember(member.Role == UserRole.CompanyOwner, member.StaffPosition);
    }
}
