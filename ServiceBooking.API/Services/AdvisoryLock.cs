using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

/// <summary>
/// Serializes concurrent requests that do a check-then-act against some shared constraint (a free
/// booking slot, a tariff seat/branch limit), so the check and the write that follows it are atomic.
/// Must be called inside an already-open transaction — pg_advisory_xact_lock releases automatically
/// when that transaction ends (commit or rollback), so callers don't need to release it explicitly.
/// </summary>
public static class AdvisoryLock
{
    public static Task AcquireAsync(AppDbContext db, string key) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");

    /// <summary>
    /// Non-blocking sibling of <see cref="AcquireAsync"/>: returns false immediately if another session
    /// holds the lock, instead of waiting for it. Used by the scheduled-task runner (ARCHITECTURE.md
    /// §8.4), where "someone else is already doing this" must mean "skip this run", not "queue behind
    /// them" — waiting would just produce the same double-run the lock exists to prevent, delayed by
    /// however long the other instance's pass takes.
    /// </summary>
    public static async Task<bool> TryAcquireAsync(AppDbContext db, string key)
    {
        var results = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({key}, 0))")
            .ToListAsync();
        return results.Single();
    }
}
