using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary><see cref="TransitionOutcome.HoldExpired"/> — the guest acted on a held booking whose timer had already run out: the expiry was finished instead (only the guest's cancel).</summary>
public enum TransitionOutcome { Ok, NotFound, VersionMismatch, InvalidTransition, HoldExpired }

public sealed record TransitionResult(TransitionOutcome Outcome, StayBooking? Booking);

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.5, §37.6.5 — every status change of a booking, in one place. A change that frees nights takes the house lock FIRST
/// (order of §37.5.1: house → booking → occupancy), then re-reads the booking. Staff actions carry <c>expectedVersion</c>; a stale version or a transition
/// the table forbids is a 409 with the CURRENT card — the action is not applied. Each change is journalled in the same transaction.
/// </summary>
public class StayBookingTransitionService(
    AppDbContext db, HouseOccupancyWriter occupancy, StayBookingEventLog eventLog, IStaysClock clock, CheckInInfoReleaser checkInInfo)
{
    // ── staff ──

    public Task<TransitionResult> ConfirmPaymentAsync(Guid companyId, Guid bookingId, int expectedVersion, StayActor actor, CancellationToken ct = default) =>
        StaffActionAsync(companyId, bookingId, expectedVersion, StayAction.ConfirmPayment, actor, reason: null, locksHouse: false, ct);

    public Task<TransitionResult> RejectPaymentAsync(Guid companyId, Guid bookingId, int expectedVersion, string reason, StayActor actor, CancellationToken ct = default) =>
        StaffActionAsync(companyId, bookingId, expectedVersion, StayAction.RejectPayment, actor, reason, locksHouse: true, ct);

    public Task<TransitionResult> CancelByOwnerAsync(Guid companyId, Guid bookingId, int expectedVersion, string reason, StayActor actor, CancellationToken ct = default) =>
        StaffActionAsync(companyId, bookingId, expectedVersion, StayAction.CancelByOwner, actor, reason, locksHouse: true, ct);

    private async Task<TransitionResult> StaffActionAsync(
        Guid companyId, Guid bookingId, int expectedVersion, StayAction action, StayActor actor, string? reason, bool locksHouse, CancellationToken ct)
    {
        var houseId = await db.StayBookings.AsNoTracking().Where(b => b.Id == bookingId && b.CompanyId == companyId).Select(b => (Guid?)b.HouseId).FirstOrDefaultAsync(ct);
        if (houseId is null) return new TransitionResult(TransitionOutcome.NotFound, null);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (locksHouse) await occupancy.LockHouseAsync(houseId.Value);
        // A fresh read AFTER the lock: the booking may have been changed by the hold-expiry task or another staff member while we waited.
        var booking = await db.StayBookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.CompanyId == companyId, ct);
        if (booking is null) return new TransitionResult(TransitionOutcome.NotFound, null);
        if (booking.Version != expectedVersion) return new TransitionResult(TransitionOutcome.VersionMismatch, Detach(booking));
        var next = StayStateMachine.Next(booking.Status, action);
        if (next is null) return new TransitionResult(TransitionOutcome.InvalidTransition, Detach(booking));

        var now = clock.UtcNow;
        var from = booking.Status;
        booking.Status = next.Value;
        booking.Version++;
        booking.UpdatedAtUtc = now;
        StayBookingEventKind kind;
        switch (action)
        {
            case StayAction.ConfirmPayment:
                booking.PaymentConfirmedAtUtc = now;
                booking.PaymentConfirmedByUserId = actor.UserId;
                booking.PaymentConfirmedByNameSnapshot = actor.NameSnapshot;
                kind = StayBookingEventKind.PaymentConfirmed;
                break;
            case StayAction.RejectPayment:
                booking.StatusReason = reason;
                booking.TerminalAtUtc = now;
                kind = StayBookingEventKind.PaymentRejected;
                break;
            default:
                booking.StatusReason = reason;
                booking.TerminalAtUtc = now;
                kind = StayBookingEventKind.CancelledByOwner;
                break;
        }
        if (StayStateMachine.IsTerminal(next.Value)) await occupancy.ReleaseBookingAsync(booking.Id, now);

        await eventLog.AppendAsync(booking, kind, actor, from, next, reason);
        if (action == StayAction.ConfirmPayment) await checkInInfo.ReleaseIfDueAsync(booking, actor: StayActor.System, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone changed the row between our read and the write (a raw conditional UPDATE of the hold-expiry task / a proof upload).
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var current = await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct);
            return new TransitionResult(TransitionOutcome.VersionMismatch, current);
        }
        await tx.CommitAsync(ct);
        return new TransitionResult(TransitionOutcome.Ok, booking);
    }

    // ── the guest ──

    /// <summary>Cancel by the link. Not allowed (final status / the check-in moment came) → <see cref="TransitionOutcome.InvalidTransition"/> with the current booking.</summary>
    public async Task<TransitionResult> CancelByGuestAsync(string token, StayActor actor, CancellationToken ct = default)
    {
        var houseId = await db.StayBookings.AsNoTracking().Where(b => b.PublicToken == token).Select(b => (Guid?)b.HouseId).FirstOrDefaultAsync(ct);
        if (houseId is null) return new TransitionResult(TransitionOutcome.NotFound, null);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await occupancy.LockHouseAsync(houseId.Value);
        var booking = await db.StayBookings.FirstOrDefaultAsync(b => b.PublicToken == token, ct);
        if (booking is null) return new TransitionResult(TransitionOutcome.NotFound, null);

        var now = clock.UtcNow;
        var checkInMoment = StayTime.ToUtc(booking.TimeZoneIdSnapshot, booking.CheckInDate, booking.CheckInTimeSnapshot);
        var holdOver = booking.Status == StayBookingStatus.Held && booking.HoldExpiresAtUtc <= now;
        if (holdOver)
        {
            // The timer ran out before the cancel (the task has not reached the booking yet): finish the expiry now, under the same house lock, so the
            // guest is told «Время на оплату истекло» and the page shows the real status — not «Время заезда наступило».
            db.Entry(booking).State = EntityState.Detached;
            await ExpireAsync(booking.Id, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new TransitionResult(TransitionOutcome.HoldExpired, null);
        }
        var next = now >= checkInMoment ? null : StayStateMachine.Next(booking.Status, StayAction.CancelByGuest);
        if (next is null) return new TransitionResult(TransitionOutcome.InvalidTransition, Detach(booking));

        var from = booking.Status;
        booking.Status = next.Value;
        booking.Version++;
        booking.TerminalAtUtc = now;
        booking.HoldExpiresAtUtc = null;
        booking.UpdatedAtUtc = now;
        await occupancy.ReleaseBookingAsync(booking.Id, now);
        await eventLog.AppendAsync(booking, StayBookingEventKind.CancelledByGuest, actor, from, next);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var current = await db.StayBookings.AsNoTracking().FirstAsync(b => b.PublicToken == token, ct);
            return new TransitionResult(TransitionOutcome.InvalidTransition, current);
        }
        await tx.CommitAsync(ct);
        return new TransitionResult(TransitionOutcome.Ok, booking);
    }

    // ── the system ──

    /// <summary>
    /// ARCHITECTURE_CYCLE37.md §37.5.3 — the hold ran out. A conditional UPDATE with the SAME clock value as the proof upload: exactly one of the two wins. The caller
    /// holds the house lock and a transaction (the task, the lazy release at creation). Returns false when the booking is no longer an expired hold.
    /// </summary>
    public async Task<bool> ExpireAsync(Guid bookingId, DateTime nowUtc, CancellationToken ct = default)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "StayBookings" SET "Status" = {(int)StayBookingStatus.ExpiredUnpaid}, "TerminalAtUtc" = {nowUtc}, "HoldExpiresAtUtc" = NULL,
                   "Version" = "Version" + 1, "UpdatedAtUtc" = {nowUtc}
            WHERE "Id" = {bookingId} AND "Status" = {(int)StayBookingStatus.Held} AND "HoldExpiresAtUtc" <= {nowUtc}
            """, ct);
        if (rows == 0) return false;
        await occupancy.ReleaseBookingAsync(bookingId, nowUtc);
        var booking = await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct);
        await eventLog.AppendAsync(booking, StayBookingEventKind.HoldExpired, StayActor.System, StayBookingStatus.Held, StayBookingStatus.ExpiredUnpaid);
        return true;
    }

    private StayBooking Detach(StayBooking booking)
    {
        db.Entry(booking).State = EntityState.Detached;
        return booking;
    }
}
