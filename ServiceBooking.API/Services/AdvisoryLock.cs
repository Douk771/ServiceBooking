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
}
