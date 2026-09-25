using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

/// <summary>
/// Single place for "can this caller manage this company" (TD-11, ARCHITECTURE_CYCLE16.md §254).
/// Five controllers had their own copy of this check and — the actual finding — the copies were NOT
/// identical: four let SuperAdmin bypass CompanyMembership, one (CompanyNotificationsController) does
/// not. Folding them naively would have changed SuperAdmin's access to notification settings, which
/// NFT §7.1 of cycle 16 forbids (no new behavior outside TD-03). So the flag is explicit at every call
/// site rather than defaulted silently.
/// </summary>
public static class CompanyAccess
{
    /// <param name="db">Database context used to check company membership.</param>
    /// <param name="user">Current caller's claims principal.</param>
    /// <param name="companyId">Company being accessed.</param>
    /// <param name="superAdminBypass">
    /// true (default) — SuperAdmin always manages the company, matching CompaniesController,
    /// ServicesController and CompanyAddressController today.
    /// false — SuperAdmin must still be a CompanyOwner member, matching CompanyNotificationsController's
    /// existing (narrower) behavior; MailingController adopts this helper with the bypass kept true,
    /// which is its own existing behavior, not a change.
    /// </param>
    public static async Task<bool> CanManageCompanyAsync(
        AppDbContext db, ClaimsPrincipal user, Guid companyId, bool superAdminBypass = true)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        if (superAdminBypass && user.IsInRole("SuperAdmin")) return true;

        return await CompanyMembership.IsOwnerAsync(db, companyId, userId);
    }
}
