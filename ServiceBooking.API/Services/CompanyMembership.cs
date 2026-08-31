using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

/// <summary>
/// The single SQL definition of "this caller actually works in this company". Everything stays a
/// server-side EXISTS — no membership row is ever materialised, so this is not a permission service,
/// just one query with one name. Callers keep their own private CanManage/CanManageCompany predicates
/// (project convention, CURRENT_STATE §6) and delegate the membership half of the rule here.
/// </summary>
public static class CompanyMembership
{
    /// <summary>Member with a role that can act on behalf of the business (Master or CompanyOwner).</summary>
    public static Task<bool> IsStaffAsync(AppDbContext db, Guid companyId, string userId) =>
        db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == userId &&
            (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner));

    /// <summary>Member with the CompanyOwner role.</summary>
    public static Task<bool> IsOwnerAsync(AppDbContext db, Guid companyId, string userId) =>
        db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == userId &&
            cm.Role == UserRole.CompanyOwner);
}
