using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.2.8-5, §37.12.1 — the ONLY writer of the booking journal and of the board revision, and the single point from which
/// notifications are planned. Rows are only added to the caller's transaction (no SaveChanges here); a failure here fails the action.
/// </summary>
public class StayBookingEventLog(AppDbContext db, StayNotificationPlanner planner, IStaysClock clock)
{
    public async Task<StayBookingEvent> AppendAsync(
        StayBooking booking, StayBookingEventKind kind, StayActor actor, StayBookingStatus? from, StayBookingStatus? to,
        string? reason = null, string? detailsJson = null, Guid? serviceSessionId = null)
    {
        var ev = new StayBookingEvent
        {
            Id = Guid.NewGuid(), StayBookingId = booking.Id, CompanyId = booking.CompanyId, Kind = kind, OccurredAtUtc = clock.UtcNow,
            ActorKind = actor.Kind, ActorUserId = actor.UserId, ActorNameSnapshot = actor.NameSnapshot, FromStatus = from, ToStatus = to,
            Reason = reason, DetailsJson = detailsJson, ServiceSessionId = serviceSessionId,
        };
        db.StayBookingEvents.Add(ev);
        await BumpRevisionAsync(booking.CompanyId);
        await planner.OnEventAsync(booking, ev);
        return ev;
    }

    /// <summary>Also used by HouseBlockWriter: the board polls this counter (`changed: false` costs one PK lookup).</summary>
    public Task BumpRevisionAsync(Guid companyId) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "StaysSettings" SET "BookingsRevision" = "BookingsRevision" + 1 WHERE "CompanyId" = {companyId}""");
}
