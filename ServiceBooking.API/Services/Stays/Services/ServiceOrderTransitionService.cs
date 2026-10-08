using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record OrderTransitionResult(TransitionOutcome Outcome, StayServiceOrder? Order);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.5.2, §39.7.5 — every status change of a stand-alone order, in one place, through the SAME <see cref="StayStateMachine"/> as a house booking.
/// Lock order: (proofs lock) → order row → session → board revision (the house lock and the lock of the service are never taken: releasing does not violate the constraint).
/// A final status frees the session in the same transaction. Staff actions carry <c>expectedVersion</c>; a stale version or a forbidden transition is a 409 with the CURRENT card.
/// </summary>
public class ServiceOrderTransitionService(AppDbContext db, ServiceSessionWriter sessions, StayServiceOrderEventLog eventLog, IStaysClock clock)
{
    public Task<OrderTransitionResult> ConfirmPaymentAsync(Guid companyId, Guid orderId, int expectedVersion, StayActor actor, CancellationToken ct = default) =>
        StaffActionAsync(companyId, orderId, expectedVersion, StayAction.ConfirmPayment, actor, reason: null, ct);

    public Task<OrderTransitionResult> RejectPaymentAsync(Guid companyId, Guid orderId, int expectedVersion, string reason, StayActor actor, CancellationToken ct = default) =>
        StaffActionAsync(companyId, orderId, expectedVersion, StayAction.RejectPayment, actor, reason, ct);

    public Task<OrderTransitionResult> CancelByOwnerAsync(Guid companyId, Guid orderId, int expectedVersion, string reason, StayActor actor, CancellationToken ct = default) =>
        StaffActionAsync(companyId, orderId, expectedVersion, StayAction.CancelByOwner, actor, reason, ct);

    private async Task<OrderTransitionResult> StaffActionAsync(
        Guid companyId, Guid orderId, int expectedVersion, StayAction action, StayActor actor, string? reason, CancellationToken ct)
    {
        if (!await db.StayServiceOrders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.CompanyId == companyId, ct)) return new OrderTransitionResult(TransitionOutcome.NotFound, null);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrderRowAsync(orderId, ct);
        // A fresh read AFTER the lock: the task or another staff member may have changed the order while we waited.
        var order = await db.StayServiceOrders.FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct);
        if (order is null) return new OrderTransitionResult(TransitionOutcome.NotFound, null);
        if (order.Version != expectedVersion) return new OrderTransitionResult(TransitionOutcome.VersionMismatch, Detach(order));
        var next = StayStateMachine.Next(order.Status, action);
        if (next is null) return new OrderTransitionResult(TransitionOutcome.InvalidTransition, Detach(order));

        var now = clock.UtcNow;
        var from = order.Status;
        order.Status = next.Value;
        order.Version++;
        order.UpdatedAtUtc = now;
        StayServiceOrderEventKind kind;
        switch (action)
        {
            case StayAction.ConfirmPayment:
                order.PaymentConfirmedAtUtc = now;
                order.PaymentConfirmedByUserId = actor.UserId;
                order.PaymentConfirmedByNameSnapshot = actor.NameSnapshot;
                kind = StayServiceOrderEventKind.PaymentConfirmed;
                break;
            case StayAction.RejectPayment:
                order.StatusReason = reason;
                order.TerminalAtUtc = now;
                kind = StayServiceOrderEventKind.PaymentRejected;
                break;
            default:
                order.StatusReason = reason;
                order.TerminalAtUtc = now;
                kind = StayServiceOrderEventKind.CancelledByOwner;
                break;
        }
        if (StayStateMachine.IsTerminal(next.Value))
            await sessions.ReleaseForOrderAsync(order.Id, action == StayAction.CancelByOwner ? StayServiceSessionState.CancelledByOwner : StayServiceSessionState.ReleasedWithOrder, now);

        await eventLog.AppendAsync(order, kind, actor, from, next, reason);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var current = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
            return new OrderTransitionResult(TransitionOutcome.VersionMismatch, current);
        }
        await tx.CommitAsync(ct);
        return new OrderTransitionResult(TransitionOutcome.Ok, order);
    }

    /// <summary>Cancel by the link. Not allowed (final status / the session has started) → <see cref="TransitionOutcome.InvalidTransition"/> with the current order.</summary>
    public async Task<OrderTransitionResult> CancelByGuestAsync(string token, StayActor actor, CancellationToken ct = default)
    {
        var id = await db.StayServiceOrders.AsNoTracking().Where(o => o.PublicToken == token).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
        if (id is null) return new OrderTransitionResult(TransitionOutcome.NotFound, null);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrderRowAsync(id.Value, ct);
        var order = await db.StayServiceOrders.FirstOrDefaultAsync(o => o.Id == id.Value, ct);
        if (order is null) return new OrderTransitionResult(TransitionOutcome.NotFound, null);
        var startUtc = await db.StayServiceSessions.AsNoTracking().Where(s => s.StayServiceOrderId == order.Id).Select(s => s.StartUtc).FirstAsync(ct);

        var now = clock.UtcNow;
        if (order.Status == StayBookingStatus.Held && order.HoldExpiresAtUtc <= now)
        {
            // The timer ran out before the cancel (the task has not reached the order yet): finish the expiry now so the guest is told «Время на оплату истекло».
            db.Entry(order).State = EntityState.Detached;
            await ExpireAsync(order.Id, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new OrderTransitionResult(TransitionOutcome.HoldExpired, null);
        }
        var next = now >= startUtc ? null : StayStateMachine.Next(order.Status, StayAction.CancelByGuest);
        if (next is null) return new OrderTransitionResult(TransitionOutcome.InvalidTransition, Detach(order));

        var from = order.Status;
        order.Status = next.Value;
        order.Version++;
        order.TerminalAtUtc = now;
        order.HoldExpiresAtUtc = null;
        order.UpdatedAtUtc = now;
        await sessions.ReleaseForOrderAsync(order.Id, StayServiceSessionState.CancelledByGuest, now);
        await eventLog.AppendAsync(order, StayServiceOrderEventKind.CancelledByGuest, actor, from, next);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new OrderTransitionResult(TransitionOutcome.InvalidTransition, await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == id.Value, ct));
        }
        await tx.CommitAsync(ct);
        return new OrderTransitionResult(TransitionOutcome.Ok, order);
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE39.md §39.7.7 — the hold of an order ran out. A conditional UPDATE with the SAME clock value as the proof upload: exactly one of the two wins. The caller
    /// holds a transaction (the task, the lazy release at creation). Returns false when the order is no longer an expired hold.
    /// </summary>
    public async Task<bool> ExpireAsync(Guid orderId, DateTime nowUtc, CancellationToken ct = default)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "StayServiceOrders" SET "Status" = {(int)StayBookingStatus.ExpiredUnpaid}, "TerminalAtUtc" = {nowUtc}, "HoldExpiresAtUtc" = NULL,
                   "Version" = "Version" + 1, "UpdatedAtUtc" = {nowUtc}
            WHERE "Id" = {orderId} AND "Status" = {(int)StayBookingStatus.Held} AND "HoldExpiresAtUtc" <= {nowUtc}
            """, ct);
        if (rows == 0) return false;
        await sessions.ReleaseForOrderAsync(orderId, StayServiceSessionState.ReleasedWithOrder, nowUtc);
        var order = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        await eventLog.AppendAsync(order, StayServiceOrderEventKind.HoldExpired, StayActor.System, StayBookingStatus.Held, StayBookingStatus.ExpiredUnpaid);
        return true;
    }

    private Task LockOrderRowAsync(Guid orderId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "StayServiceOrders" WHERE "Id" = {orderId} FOR UPDATE""", ct);

    private StayServiceOrder Detach(StayServiceOrder order)
    {
        db.Entry(order).State = EntityState.Detached;
        return order;
    }
}

/// <summary>ARCHITECTURE_CYCLE39.md §39.7.7 — the second pass of <c>stays-hold-expiry</c>: orders whose hold ran out, in batches of 50, each in its own transaction.</summary>
public class ServiceOrderHoldExpirer(AppDbContext db, ServiceOrderTransitionService transitions, IStaysClock clock)
{
    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await db.StayServiceOrders.AsNoTracking().Where(o => o.Status == StayBookingStatus.Held && o.HoldExpiresAtUtc <= now)
            .OrderBy(o => o.HoldExpiresAtUtc).Select(o => o.Id).Take(batchSize).ToListAsync(ct);
        var expired = 0;
        foreach (var id in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await transitions.ExpireAsync(id, now, ct)) expired++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return expired;
    }
}
