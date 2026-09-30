using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §576 — nothing is ever sent to (or queued for) a fictional company's clients or staff: no WhatsApp/MAX message, no web push,
/// no mailing. Holds on the mark alone, so no machine configuration can make a showcase company send. In demo mode (pass B) it holds for every company,
/// including the ones visitors create.
///
/// Called at the three places rows are queued (booking notifications, master push, mailing) and as a safety net in the two dispatch tasks.
/// </summary>
public sealed class ShowcaseOutboundGuard(AppDbContext db, IOptions<DemoModeOptions> demo)
{
    /// <summary>True when the instance runs in demo mode.</summary>
    public bool DemoMode => demo.Value.Enabled;

    /// <summary>Pure rule for callers that already loaded the company.</summary>
    public static bool IsSuppressed(Company company, bool demoMode) => demoMode || company.IsShowcase;

    /// <summary>Whether nothing must be queued for this company. Unknown company: not suppressed (the caller's own not-found handling applies).</summary>
    public async Task<bool> IsSuppressedAsync(Guid companyId, CancellationToken ct)
    {
        if (DemoMode) return true;
        return await db.Companies.AsNoTracking().AnyAsync(c => c.Id == companyId && c.IsShowcase, ct);
    }

    /// <summary>The subset of <paramref name="companyIds"/> whose queued rows must be skipped at send time (dispatch tasks' safety net).</summary>
    public async Task<HashSet<Guid>> SuppressedCompanyIdsAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken ct)
    {
        if (companyIds.Count == 0) return [];
        if (DemoMode) return companyIds.ToHashSet();
        return (await db.Companies.AsNoTracking()
            .Where(c => companyIds.Contains(c.Id) && c.IsShowcase)
            .Select(c => c.Id)
            .ToListAsync(ct)).ToHashSet();
    }
}
