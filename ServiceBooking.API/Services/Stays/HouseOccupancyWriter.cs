using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.5 — the ONLY writer of <c>HouseOccupancies</c>. Protection in three levels: the PostgreSQL EXCLUDE constraint
/// (<c>EX_HouseOccupancies_NoOverlap</c>, 23P01) is the last and unbreakable one; the advisory lock <c>stay-house:{houseId}</c> serialises everything that
/// changes a house's occupancy so the rules the constraint cannot express (gap, lazy hold release, prices) are checked consistently; the booking
/// <c>Version</c> guards staff actions.
/// </summary>
public class HouseOccupancyWriter(AppDbContext db)
{
    public const string OverlapConstraint = "EX_HouseOccupancies_NoOverlap";

    public Task LockHouseAsync(Guid houseId) => AdvisoryLock.AcquireAsync(db, $"stay-house:{houseId}");

    /// <summary>Unreleased periods of the house that intersect or TOUCH [from, to] (neighbours are needed by the gap rule). Expiry is judged by the rules.</summary>
    public async Task<List<OccupiedPeriod>> LoadActiveAsync(Guid houseId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await db.HouseOccupancies.AsNoTracking()
            .Where(o => o.HouseId == houseId && o.ReleasedAtUtc == null && o.StartDate <= to && o.EndDate >= from)
            .Select(o => new OccupiedPeriod(o.StartDate, o.EndDate, o.HoldExpiresAtUtc)).ToListAsync(ct);

    public HouseOccupancy AddBooking(StayBooking booking, DateTime nowUtc)
    {
        var o = new HouseOccupancy
        {
            Id = Guid.NewGuid(), CompanyId = booking.CompanyId, HouseId = booking.HouseId, StartDate = booking.CheckInDate, EndDate = booking.CheckOutDate,
            Source = OccupancySource.PlatformBooking, StayBookingId = booking.Id, HoldExpiresAtUtc = booking.HoldExpiresAtUtc, CreatedAtUtc = nowUtc,
        };
        db.HouseOccupancies.Add(o);
        return o;
    }

    public HouseOccupancy AddBlock(HouseBlock block, DateTime nowUtc)
    {
        var o = new HouseOccupancy
        {
            Id = Guid.NewGuid(), CompanyId = block.CompanyId, HouseId = block.HouseId, StartDate = block.StartDate, EndDate = block.EndDate,
            Source = OccupancySource.OwnerBlock, HouseBlockId = block.Id, CreatedAtUtc = nowUtc,
        };
        db.HouseOccupancies.Add(o);
        return o;
    }

    /// <summary>The booking reached a final status: its nights are free again (same transaction as the status change).</summary>
    public Task ReleaseBookingAsync(Guid bookingId, DateTime nowUtc) =>
        db.HouseOccupancies.Where(o => o.StayBookingId == bookingId && o.ReleasedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReleasedAtUtc, nowUtc).SetProperty(o => o.HoldExpiresAtUtc, (DateTime?)null));

    /// <summary>The payment proof arrived in time: the hold timer disappears from the occupancy row too (invariant §37.2.8-4).</summary>
    public Task ClearHoldAsync(Guid bookingId) =>
        db.HouseOccupancies.Where(o => o.StayBookingId == bookingId && o.ReleasedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.HoldExpiresAtUtc, (DateTime?)null));

    public Task ReleaseBlockAsync(Guid blockId, DateTime nowUtc) =>
        db.HouseOccupancies.Where(o => o.HouseBlockId == blockId && o.ReleasedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReleasedAtUtc, nowUtc));

    /// <summary>Is this exactly the "two unreleased periods share a night" violation of the database?</summary>
    public static bool IsOverlapViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } }
        || ex is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };
}
