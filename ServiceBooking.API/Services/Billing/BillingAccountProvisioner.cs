using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 5 (ARCHITECTURE_CYCLE5.md §43.2/§45.1) — the one place that turns "a user who pays" into a
/// <see cref="BillingAccount"/> row. Accounts are provisioned on demand rather than at registration:
/// the first thing that needs one (today: creating the first company) calls
/// <see cref="EnsureAccountAsync"/> instead of every sign-up growing a row nobody uses.
/// </summary>
public class BillingAccountProvisioner(AppDbContext db)
{
    /// <summary>
    /// Returns the caller's existing billing account id, or creates one. Does not call
    /// <c>SaveChangesAsync</c> itself when nothing new was added other than the account row, so it can
    /// be composed into a caller's own transaction/SaveChanges (e.g. CompaniesController.Create, which
    /// saves the new account together with the new company in one round trip).
    /// </summary>
    public async Task<Guid> EnsureAccountAsync(string ownerUserId)
    {
        var existingId = await db.BillingAccounts
            .Where(a => a.OwnerUserId == ownerUserId)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync();
        if (existingId.HasValue) return existingId.Value;

        var account = new BillingAccount
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
        };
        db.BillingAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>Read-only lookup used by money-reading paths that must NOT provision a new account just
    /// to answer "what plan does this user have" (e.g. a user who never created a company yet) — those
    /// callers fall back to <see cref="ServiceBooking.API.Services.EffectivePlan.Free"/> instead.</summary>
    public static Task<Guid?> FindAccountIdAsync(AppDbContext db, string ownerUserId) =>
        db.BillingAccounts.Where(a => a.OwnerUserId == ownerUserId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync();
}
