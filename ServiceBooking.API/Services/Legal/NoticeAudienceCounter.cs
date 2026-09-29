using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// API_CONTRACT_CYCLE20.md §434.4/§434.5/§434.6 — the CURRENT number of addressees for a given audience
/// selection, used by the admin notices list (<c>audienceCount</c>), the publish response, and the
/// preview endpoint. Deliberately separate from <see cref="NoticeAudience"/>, which answers "does THIS
/// ONE caller match" — this answers "how many accounts/users match right now", the admin-facing mirror of
/// the same rule (§404.2: "число адресатов в админке — текущее, а не на момент публикации").
/// </summary>
public sealed class NoticeAudienceCounter(AppDbContext db, UserManager<AppUser> userManager)
{
    public async Task<int> CountAsync(NoticeAudienceType type, Guid[]? planIds, Guid? billingAccountId, CancellationToken ct = default)
    {
        switch (type)
        {
            case NoticeAudienceType.AllOwners:
                return await db.BillingAccounts.CountAsync(ct);

            case NoticeAudienceType.OwnersOnPlans:
                return await CountOwnersOnPlansAsync(planIds ?? [], ct);

            case NoticeAudienceType.BillingAccount:
                return await db.BillingAccounts.CountAsync(a => a.Id == billingAccountId, ct);

            case NoticeAudienceType.AllClients:
                return await CountAllClientsAsync(ct);

            default:
                return 0;
        }
    }

    private async Task<int> CountOwnersOnPlansAsync(Guid[] planIds, CancellationToken ct)
    {
        if (planIds.Length == 0) return 0;

        var systemFreeInList = await db.SubscriptionPlanConfigs.AsNoTracking()
            .AnyAsync(p => p.IsSystemFree && planIds.Contains(p.Id), ct);

        // Standard EF Core "left join" shape (GroupJoin + SelectMany + DefaultIfEmpty) — an account has
        // at most one AccountSubscription row (BillingAccountId is unique there), so this never
        // duplicates an account. Materialized locally: the account count is small enough (§404.2's own
        // "единицы и десятки строк" reasoning extends here — this is a SuperAdmin-only, low-traffic
        // screen) that matching the OwnersOnPlans rule (including the "no subscription row at all"
        // system-Free case) is far simpler done in memory than as one gnarly SQL predicate.
        var pairs = await db.BillingAccounts.AsNoTracking()
            .GroupJoin(db.AccountSubscriptions.AsNoTracking(), a => a.Id, s => s.BillingAccountId, (a, subs) => new { a.Id, subs })
            .SelectMany(x => x.subs.DefaultIfEmpty(), (x, s) => new { x.Id, PlanConfigId = s == null ? (Guid?)null : s.PlanConfigId })
            .ToListAsync(ct);

        return pairs.Count(p => (p.PlanConfigId is { } pcid && planIds.Contains(pcid)) || (p.PlanConfigId is null && systemFreeInList));
    }

    private async Task<int> CountAllClientsAsync(CancellationToken ct)
    {
        var totalActive = await db.Users.CountAsync(u => u.DeletedAtUtc == null, ct);
        var superAdmins = await userManager.GetUsersInRoleAsync("SuperAdmin");
        var activeSuperAdmins = superAdmins.Count(u => u.DeletedAtUtc == null);
        return totalActive - activeSuperAdmins;
    }
}
