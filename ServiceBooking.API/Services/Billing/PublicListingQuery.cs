using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE9.md §103.5 — the SQL-side twin of <see cref="SubscriptionResolver"/>'s
/// <c>AllowPublicListing</c> rule, kept as a single extension method so <c>GET /api/companies/public</c>
/// can filter/paginate entirely in the database instead of materializing every active company and
/// filtering/paging in memory (the regression §103.5 calls out by name).
///
/// This mirrors exactly the subset of <see cref="SubscriptionResolver"/>'s plan-resolution rule that determines
/// <c>EffectivePlan.AllowPublicListing</c>: a company's effective "allow public listing" flag is
/// <c>true</c> (the Free baseline) unless its billing account has a subscription row that is both
/// currently usable (<c>IsActive</c> and not past <c>PaidUntil</c>) AND bound to a currently-active
/// plan config — in which case the flag is that plan's own <c>AllowPublicListing</c> column. Employee/
/// company-seat bonuses and notification-channel purchases never affect this flag, so they are
/// deliberately not considered here (unlike the full <see cref="EffectivePlan"/> resolution).
///
/// ⚠️ If <see cref="SubscriptionResolver"/>'s rule for this flag ever changes, this method must change
/// with it — the two are required to agree by construction (§103.5's own call-out: divergence here is
/// exactly what the paired functional test, comparing <c>GET /api/companies</c> and
/// <c>GET /api/companies/public</c> on the same data, exists to catch).
/// </summary>
public static class PublicListingQuery
{
    /// <summary>
    /// Filters an <see cref="IQueryable{Company}"/> down to companies whose effective plan currently
    /// allows public listing, expressed so EF Core translates it to a single correlated subquery (no
    /// N+1, no client-side evaluation). Callers are still responsible for the other two halves of
    /// "publicly listed" — <c>Company.IsActive</c> and <c>Company.ShowInPublicListing</c> — which stay
    /// plain <c>Where</c> clauses on the caller's own query, same as before.
    /// </summary>
    public static IQueryable<Company> WhereAllowsPublicListing(
        this IQueryable<Company> companies, AppDbContext db, DateTime nowUtc) =>
        companies.Where(c =>
            (db.AccountSubscriptions
                .Where(s => s.BillingAccountId == c.BillingAccountId)
                .Select(s => (bool?)(
                    s.IsActive
                    && (!s.PaidUntil.HasValue || s.PaidUntil >= nowUtc)
                    && s.PlanConfig != null && s.PlanConfig.IsActive
                        ? s.PlanConfig.AllowPublicListing
                        : true))
                .FirstOrDefault()) ?? true);
}
