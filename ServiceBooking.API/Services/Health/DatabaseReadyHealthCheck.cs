using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Health;

/// <summary>
/// Backs GET /api/health/ready (ARCHITECTURE.md §10.2). Readiness means two things: PostgreSQL is
/// reachable, and every migration has been applied. The second check is meaningfully more expensive
/// than the first (it enumerates migration history) and only matters during the start-up window —
/// migrations run once, at boot, in Program.cs, so once a check has observed zero pending migrations
/// that fact cannot become false again for the lifetime of the process. The singleton flag below turns
/// an O(migrations) check that would otherwise run every 30 seconds forever into a one-time cost.
/// </summary>
public class DatabaseReadyHealthCheck(AppDbContext db) : IHealthCheck
{
    // Deliberately a plain static field, not a DI singleton service: the whole point is "the very first
    // successful check latches this for the rest of the process", which is exactly what a static field
    // does and a scoped/singleton service would only complicate (this check itself is registered once
    // and reused, but the flag needs to survive across separate invocations regardless of DI lifetime).
    private static volatile bool _migrationsConfirmedApplied;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Whatever the failure is (connection refused, wrong credentials, DNS), the exception itself
        // never reaches the response — ResponseWriter in Program.cs only ever emits "database" as the
        // failed check name (US-43 p.3: no exception text, no stack trace, no connection string).
        bool canConnect;
        try
        {
            canConnect = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            canConnect = false;
        }

        if (!canConnect)
            return HealthCheckResult.Unhealthy("database");

        if (_migrationsConfirmedApplied)
            return HealthCheckResult.Healthy();

        try
        {
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Any())
                return HealthCheckResult.Unhealthy("migrations");

            _migrationsConfirmedApplied = true;
            return HealthCheckResult.Healthy();
        }
        catch
        {
            // Can connect but can't enumerate migration history (e.g. permissions) — treat as a database
            // problem rather than surfacing a third failure reason the contract doesn't define.
            return HealthCheckResult.Unhealthy("database");
        }
    }
}
