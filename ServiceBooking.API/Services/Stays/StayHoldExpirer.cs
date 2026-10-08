using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>ARCHITECTURE_CYCLE37.md §37.5.2, §37.7.3 — turns expired holds into "Снята: не оплачена", by the task (batches) and lazily at creation.</summary>
public class StayHoldExpirer(AppDbContext db, HouseOccupancyWriter occupancy, StayBookingTransitionService transitions, IStaysClock clock)
{
    /// <summary>Expired held bookings of one house that touch the requested dates — inside the caller's transaction and house lock (lazy release, §37.5.2).</summary>
    public async Task ExpireOverlappingAsync(Guid houseId, DateOnly checkIn, DateOnly checkOut, DateTime nowUtc, CancellationToken ct = default)
    {
        var due = await db.StayBookings.AsNoTracking()
            .Where(b => b.HouseId == houseId && b.Status == StayBookingStatus.Held && b.HoldExpiresAtUtc <= nowUtc && b.CheckInDate < checkOut && checkIn < b.CheckOutDate)
            .Select(b => b.Id).ToListAsync(ct);
        foreach (var id in due) await transitions.ExpireAsync(id, nowUtc, ct);
    }

    /// <summary>The task's pass: up to <paramref name="batchSize"/> due holds, each in its own transaction under its house lock. Returns how many were expired.</summary>
    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await db.StayBookings.AsNoTracking()
            .Where(b => b.Status == StayBookingStatus.Held && b.HoldExpiresAtUtc <= now)
            .OrderBy(b => b.HoldExpiresAtUtc).Select(b => new { b.Id, b.HouseId }).Take(batchSize).ToListAsync(ct);
        var expired = 0;
        foreach (var item in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await occupancy.LockHouseAsync(item.HouseId);
            if (await transitions.ExpireAsync(item.Id, now, ct)) expired++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return expired;
    }
}
