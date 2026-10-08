using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.9.2 — the ONLY writer of the journal of a stand-alone order, the same shape as <see cref="StayBookingEventLog"/>: it adds the event, bumps the
/// board revision and is the single point from which the notifications of the order are planned. Rows are only added to the caller's transaction (no SaveChanges).
/// </summary>
public class StayServiceOrderEventLog(AppDbContext db, StayNotificationPlanner planner, StayBookingEventLog bookingLog, IStaysClock clock)
{
    public async Task<StayServiceOrderEvent> AppendAsync(
        StayServiceOrder order, StayServiceOrderEventKind kind, StayActor actor, StayBookingStatus? from, StayBookingStatus? to,
        string? reason = null, string? detailsJson = null)
    {
        var ev = new StayServiceOrderEvent
        {
            Id = Guid.NewGuid(), StayServiceOrderId = order.Id, CompanyId = order.CompanyId, Kind = kind, OccurredAtUtc = clock.UtcNow, ActorKind = actor.Kind,
            ActorUserId = actor.UserId, ActorNameSnapshot = actor.NameSnapshot, FromStatus = from, ToStatus = to, Reason = reason, DetailsJson = detailsJson,
        };
        db.StayServiceOrderEvents.Add(ev);
        await bookingLog.BumpRevisionAsync(order.CompanyId);
        await planner.OnOrderEventAsync(order, ev);
        return ev;
    }
}
