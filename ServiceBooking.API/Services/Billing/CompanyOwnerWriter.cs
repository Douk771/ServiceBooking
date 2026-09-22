using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 7 (ARCHITECTURE_CYCLE7.md §50, §51.3) — the single place that moves
/// <see cref="Company.OwnerUserId"/> and writes <see cref="CompanyOwnerChangeLog"/>. Used by BOTH
/// <c>AdminController.UpdateCompanyOwner</c> (a stand-alone owner change, <c>WithTransfer = false</c>)
/// and <c>CompanyTransferService</c> (an owner change bundled with a company transfer,
/// <c>WithTransfer = true</c>), so the two call sites can never drift apart on roles, demotion of the
/// old owner, or the audit trail (§59's grep 8: after this class exists, <c>OwnerUserId</c> is assigned
/// to nowhere else in the codebase).
///
/// Deliberately does NOT call <c>SaveChangesAsync</c> or <see cref="IdentityRoleSync"/> itself — both
/// callers need to do other work (channel unassignment, BillingAccountId, subscription-log rows) in the
/// SAME SaveChanges round trip, and IdentityRoleSync must run after that save has landed
/// (IdentityRoleSync's own contract). Callers are responsible for the transaction and the
/// company-members advisory lock (§52) around this call.
/// </summary>
public class CompanyOwnerWriter(AppDbContext db)
{
    /// <summary>
    /// Moves ownership of <paramref name="company"/> to <paramref name="newOwnerUserId"/>: demotes the
    /// old owner's CompanyMember row (if any) to Master, promotes/creates the new owner's row to
    /// CompanyOwner, and appends one <see cref="CompanyOwnerChangeLog"/> row. Returns the old owner's
    /// user id so the caller can run <see cref="IdentityRoleSync"/> for both users after SaveChanges.
    /// Caller must ensure <paramref name="newOwnerUserId"/> already passed whatever validation applies
    /// to its call site (existence/tombstone/§51.1 linkage) — this class trusts its input.
    /// </summary>
    public async Task<string> ChangeOwnerAsync(
        Company company, string newOwnerUserId, string changedByUserId, bool withTransfer, string? comment = null)
    {
        var oldOwnerUserId = company.OwnerUserId;
        company.OwnerUserId = newOwnerUserId;

        if (oldOwnerUserId != newOwnerUserId)
        {
            var oldMembership = await db.CompanyMembers.FirstOrDefaultAsync(cm =>
                cm.CompanyId == company.Id && cm.UserId == oldOwnerUserId && cm.Role == UserRole.CompanyOwner);
            if (oldMembership is not null)
                oldMembership.Role = UserRole.Master;
        }

        var membership = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.CompanyId == company.Id && cm.UserId == newOwnerUserId);
        if (membership is null)
            db.CompanyMembers.Add(new CompanyMember { Id = Guid.NewGuid(), CompanyId = company.Id, UserId = newOwnerUserId, Role = UserRole.CompanyOwner });
        else
            membership.Role = UserRole.CompanyOwner;

        db.CompanyOwnerChangeLogs.Add(new CompanyOwnerChangeLog
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            OldOwnerUserId = oldOwnerUserId,
            NewOwnerUserId = newOwnerUserId,
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = DateTime.UtcNow,
            Comment = comment,
            WithTransfer = withTransfer,
        });

        return oldOwnerUserId;
    }
}
