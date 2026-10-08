using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.5.2 — the lazy release under the lock of a service: a hold that has already run out but is still in the way of a new start. An ORDER is released by the
/// same conditional UPDATE as the task (its row comes after the lock of the service in the order of locks). A BOOKING needs the lock of its HOUSE, which stands EARLIER than the lock of
/// a service — so only a non-blocking try-lock is taken: it wins → the booking is expired (nights and sessions); it does not → the time simply stays taken (409 SlotTaken, the task
/// frees it within 15 s). The row of the hold is taken with FOR UPDATE SKIP LOCKED — a row somebody else is working on (a staff action, a proof upload) is not waited for either,
/// and the bump of the board revision is deferred to the write of the transaction (<see cref="AppDbContext.BumpRevisionAsync"/>), so nothing here waits and a lock cycle cannot form.
/// </summary>
public class ServiceHoldReleaser(AppDbContext db, ServiceSlotService slots, ServiceOrderTransitionService orders, StayBookingTransitionService bookings, IStaysClock clock)
{
    public async Task ReleaseExpiredAsync(ServiceScope scope, DateOnly date, CancellationToken ct)
    {
        var (expiredOrders, expiredBookings) = await slots.ExpiredHoldsAsync(scope, date, ct);
        var now = clock.UtcNow;
        foreach (var orderId in expiredOrders)
            if (await TakeRowAsync("StayServiceOrders", orderId, ct)) await orders.ExpireAsync(orderId, now, ct);
        foreach (var (bookingId, houseId) in expiredBookings)
            if (await AdvisoryLock.TryAcquireAsync(db, $"stay-house:{houseId}") && await TakeRowAsync("StayBookings", bookingId, ct)) await bookings.ExpireAsync(bookingId, now, ct);
    }

    /// <summary>Locks the row of a still-held parent without waiting; false — somebody holds it, the time stays taken.</summary>
    private async Task<bool> TakeRowAsync(string table, Guid id, CancellationToken ct)
    {
        var rows = table == "StayBookings"
            ? await db.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "StayBookings" WHERE "Id" = {id} AND "Status" = 0 FOR UPDATE SKIP LOCKED""").ToListAsync(ct)
            : await db.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "StayServiceOrders" WHERE "Id" = {id} AND "Status" = 0 FOR UPDATE SKIP LOCKED""").ToListAsync(ct);
        return rows.Count > 0;
    }
}
