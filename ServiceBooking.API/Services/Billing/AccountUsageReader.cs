using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 5 (ARCHITECTURE_CYCLE5.md §46) — how much of a billing account's SUMMED limits are already
/// spent: companies owned, and seats occupied across every one of them. Two grouped queries total, no
/// matter how many accounts/companies are asked about — the same "one query for the whole page" shape
/// as <c>CompaniesController.GetReviewAggregatesAsync</c>/<c>GetCitiesAsync</c> (cycle 4). Deliberately
/// NOT cached (§46.3): a stale spend counter is a silently permitted limit overrun, not a performance
/// bug — only the (unrelated) pricing catalog is cached, per §48.
/// </summary>
public sealed record AccountUsage(Guid BillingAccountId, int CompaniesUsed, int SeatsUsed);

public class AccountUsageReader(AppDbContext db)
{
    /// <summary>
    /// One grouped query for however many accounts are asked about (§46.1's exact SQL): companies
    /// owned and seats occupied, summed per billing account. An account with zero companies (shouldn't
    /// happen in practice, but defensive) is simply absent from the result — callers get 0 via
    /// <c>GetValueOrDefault</c>/<c>TryGetValue</c>, same convention as <c>GetReviewAggregatesAsync</c>.
    /// </summary>
    public async Task<Dictionary<Guid, AccountUsage>> GetAsync(IEnumerable<Guid> accountIds)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, AccountUsage>();

        var rows = await db.Set<AccountUsageRow>()
            .FromSqlInterpolated($"""
                SELECT c."BillingAccountId" AS "BillingAccountId",
                       COUNT(DISTINCT c."Id")::int AS "CompaniesUsed",
                       COUNT(cm."Id")::int AS "SeatsUsed"
                FROM "Companies" c
                LEFT JOIN "CompanyMembers" cm ON cm."CompanyId" = c."Id"
                WHERE c."BillingAccountId" = ANY({ids.ToArray()})
                GROUP BY c."BillingAccountId"
                """)
            .ToListAsync();

        return rows.ToDictionary(r => r.BillingAccountId, r => new AccountUsage(r.BillingAccountId, r.CompaniesUsed, r.SeatsUsed));
    }

    /// <summary>
    /// Second grouped query (§46.1): per-company employee count (`CompanyDto.employeeCount`, the
    /// LOCAL figure shown next to the account-wide `accountSeatsUsed`) — one `GROUP BY CompanyId` for
    /// the whole page, not one query per company.
    /// </summary>
    public async Task<Dictionary<Guid, int>> GetCompanySeatsAsync(IEnumerable<Guid> companyIds)
    {
        var ids = companyIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, int>();

        return await db.CompanyMembers
            .Where(cm => ids.Contains(cm.CompanyId))
            .GroupBy(cm => cm.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count);
    }
}
