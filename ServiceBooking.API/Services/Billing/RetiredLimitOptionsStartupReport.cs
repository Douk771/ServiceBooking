using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE19.md §385.5 — the second line of defence behind the deploy-time gate
/// (deploy/deploy-remote.sh's check_retired_limit_options): logged once, right after
/// <c>db.Database.MigrateAsync()</c>, in the same startup block that seeds roles. Closes the race window
/// between the gate running and the app actually restarting (a row created in the OLD app in those few
/// seconds), and catches any start that bypassed deploy-remote.sh entirely. The start itself is never
/// blocked — this only makes an otherwise-invisible, already-impossible-after-the-gate state loud enough
/// to reach GlitchTip.
/// </summary>
public static class RetiredLimitOptionsStartupReport
{
    public static async Task LogAsync(AppDbContext db, ILogger logger)
    {
        var now = DateTime.UtcNow;
        var live = await RetiredLimitOptions.LiveRetiredRows(db, now)
            .Include(o => o.Option)
            .Select(o => new { o.BillingAccountId, o.Option.Code, o.Quantity })
            .ToListAsync();

        if (live.Count == 0)
        {
            logger.LogInformation("Cycle 19: live rows of retired limit options: 0");
            return;
        }

        // LogError, not a thrown exception — this state should be impossible right after a successful
        // deploy-time gate (§385.3), but it must never silently vanish into an Information line either.
        // No PII: account ids and option codes only, same convention as the deploy log report (§385.4).
        logger.LogError(
            "Cycle 19: {Count} live row(s) of retired limit options found at startup — this should be " +
            "impossible right after the deploy gate passed (ARCHITECTURE_CYCLE19.md §385.5/§385.6). " +
            "Rows: {Rows}",
            live.Count,
            string.Join(", ", live.Select(r => $"{r.BillingAccountId} / {r.Code} × {r.Quantity}")));
    }
}
