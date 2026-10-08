using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.5.2 — the lazy release under the lock of a service: a hold that has already run out but is still in the way of a new start. An ORDER is released by the
/// same conditional UPDATE as the task (its row comes after the lock of the service in the order of locks). A BOOKING needs the lock of its HOUSE, which stands EARLIER than the lock of
/// a service — so only a non-blocking try-lock is taken: it wins → the booking is expired (nights and sessions); it does not → the time simply stays taken (409 SlotTaken, the task
/// frees it within 15 s). Nothing ever waits, so a deadlock is impossible.
/// </summary>
public class ServiceHoldReleaser(AppDbContext db, ServiceSlotService slots, ServiceOrderTransitionService orders, StayBookingTransitionService bookings, IStaysClock clock)
{
    public async Task ReleaseExpiredAsync(ServiceScope scope, DateOnly date, CancellationToken ct)
    {
        var (expiredOrders, expiredBookings) = await slots.ExpiredHoldsAsync(scope, date, ct);
        var now = clock.UtcNow;
        foreach (var orderId in expiredOrders) await orders.ExpireAsync(orderId, now, ct);
        foreach (var (bookingId, houseId) in expiredBookings)
            if (await AdvisoryLock.TryAcquireAsync(db, $"stay-house:{houseId}")) await bookings.ExpireAsync(bookingId, now, ct);
    }
}
