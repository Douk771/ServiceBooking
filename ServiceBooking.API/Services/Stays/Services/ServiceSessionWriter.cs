using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>Everything a new session is made of; the writer only fills the keys, the interval and the state.</summary>
public sealed record NewServiceSession(
    Guid CompanyId, StayService Service, Guid? StayBookingId, Guid? StayServiceOrderId, ServiceEvaluation Evaluation, StayActorKind AddedByKind,
    string? AddedByUserId, string? AddedByNameSnapshot, StayServiceRequestBasis? RequestBasis, string? AddNoticeVersion, Guid? IdempotencyKey);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.5 — the ONLY writer of <c>StayServiceSessions</c> (the occupancy of a service). Protection in three levels, as for nights: the PostgreSQL
/// EXCLUDE constraint <c>EX_StayServiceSessions_NoOverlap</c> (real moments, 23P01) is the last and unbreakable one; the advisory lock <c>stay-service:{serviceId}</c>
/// serialises everything that OCCUPIES the time of a service (releasing never takes it); the <c>Version</c> columns guard the staff's actions.
/// </summary>
public class ServiceSessionWriter(AppDbContext db)
{
    public const string OverlapConstraint = "EX_StayServiceSessions_NoOverlap";

    public Task LockServiceAsync(Guid serviceId) => AdvisoryLock.AcquireAsync(db, $"stay-service:{serviceId}");

    /// <summary>Several services always in ascending order of the id as a string (the one order of ARCHITECTURE_CYCLE39.md §39.5.2): no two requests wait for each other.</summary>
    public async Task LockServicesAsync(IEnumerable<Guid> serviceIds)
    {
        foreach (var id in serviceIds.Distinct().OrderBy(i => i.ToString(), StringComparer.Ordinal)) await LockServiceAsync(id);
    }

    public StayServiceSession Add(NewServiceSession spec, DateTime nowUtc)
    {
        var e = spec.Evaluation;
        var session = new StayServiceSession
        {
            Id = Guid.NewGuid(), CompanyId = spec.CompanyId, ServiceId = spec.Service.Id, StayBookingId = spec.StayBookingId, StayServiceOrderId = spec.StayServiceOrderId,
            BusinessDate = e.BusinessDate, StartMinute = e.StartMinute, Hours = e.Hours, StartUtc = e.StartUtc, EndUtc = e.EndUtc,
            BufferMinutesSnapshot = spec.Service.BufferMinutes, OccupiedUntilUtc = e.EndUtc.AddMinutes(spec.Service.BufferMinutes),
            State = StayServiceSessionState.Active, ServiceNameSnapshot = spec.Service.Name,
            HourPricesJson = ServiceJson.HourPrices(e.StartMinute, e.HourPrices!), ItemsJson = ServiceJson.Items(e.Items),
            ServiceAmountRub = e.Money!.ServiceAmountRub, ItemsAmountRub = e.Money.ItemsAmountRub, TotalRub = e.Money.TotalRub,
            AddedByKind = spec.AddedByKind, AddedByUserId = spec.AddedByUserId, AddedByNameSnapshot = spec.AddedByNameSnapshot, RequestBasis = spec.RequestBasis,
            AddNoticeVersion = spec.AddNoticeVersion, IdempotencyKey = spec.IdempotencyKey, Version = 1, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc,
        };
        db.StayServiceSessions.Add(session);
        return session;
    }

    /// <summary>The booking reached a final status: its sessions are free again (the same transaction as the status change). Returns the ids released.</summary>
    public async Task<List<Guid>> ReleaseForBookingAsync(Guid bookingId, DateTime nowUtc)
    {
        var ids = await db.StayServiceSessions.Where(s => s.StayBookingId == bookingId && s.ReleasedAtUtc == null).Select(s => s.Id).ToListAsync();
        if (ids.Count == 0) return ids;
        await db.StayServiceSessions.Where(s => ids.Contains(s.Id) && s.ReleasedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.State, StayServiceSessionState.ReleasedWithBooking).SetProperty(s => s.ReleasedAtUtc, nowUtc).SetProperty(s => s.UpdatedAtUtc, nowUtc));
        return ids;
    }

    /// <summary>The order reached a final status: its one session is free again. <paramref name="state"/> says why (cancelled by the guest / by the company / released with the order).</summary>
    public Task<int> ReleaseForOrderAsync(Guid orderId, StayServiceSessionState state, DateTime nowUtc) =>
        db.StayServiceSessions.Where(s => s.StayServiceOrderId == orderId && s.ReleasedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.State, state).SetProperty(s => s.ReleasedAtUtc, nowUtc).SetProperty(s => s.UpdatedAtUtc, nowUtc));

    /// <summary>Cancels one session of a booking (by the guest or by the company): frees the time and keeps the snapshot.</summary>
    public void Cancel(StayServiceSession session, StayServiceSessionState state, string? reason, DateTime nowUtc)
    {
        session.State = state;
        session.ReleasedAtUtc = nowUtc;
        session.StatusReason = reason;
        session.Version++;
        session.UpdatedAtUtc = nowUtc;
    }

    /// <summary>Is this exactly the «two unreleased sessions of a service share a moment» violation of the database?</summary>
    public static bool IsOverlapViolation(Exception ex) =>
        (ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } pg } && pg.ConstraintName == OverlapConstraint)
        || (ex is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } direct && direct.ConstraintName == OverlapConstraint);
}
