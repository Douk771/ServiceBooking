using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record OrderProofResult(ProofOutcome Outcome, StayServiceOrder? Order = null, string? Error = null);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.2.2, §39.23.2 — payment proofs of a stand-alone order. The file is read and checked by <see cref="StayPaymentProofService.ReadAsync"/> (by its BYTES,
/// images re-encoded) and kept in the PRIVATE storage; the hold-expiry race is decided by ONE conditional UPDATE with the same clock as the task. Lock order: the lock of the
/// order's proofs → the order row → the proof → the board revision.
/// </summary>
public class ServiceOrderProofService(
    AppDbContext db, FileStorage storage, StayPaymentProofService reader, StayServiceOrderEventLog eventLog, ServiceOrderTransitionService transitions,
    IStaysClock clock, IOptions<StaysOptions> options)
{
    public async Task<OrderProofResult> AttachAsync(string token, IFormFile? upload, CancellationToken ct = default)
    {
        var found = await db.StayServiceOrders.AsNoTracking().Where(o => o.PublicToken == token).Select(o => new { o.Id, o.CompanyId }).FirstOrDefaultAsync(ct);
        if (found is null) return new OrderProofResult(ProofOutcome.NotFound);

        var (file, error) = await reader.ReadAsync(upload);
        if (file is null) return new OrderProofResult(ProofOutcome.BadFile, Error: error);

        var key = await storage.SavePrivateAsync(found.CompanyId, file.Bytes, file.Extension);
        try
        {
            var result = await AttachCoreAsync(found.Id, found.CompanyId, key, file, ct);
            if (result.Outcome != ProofOutcome.Ok) storage.DeletePrivate(key);
            return result;
        }
        catch
        {
            storage.DeletePrivate(key);
            throw;
        }
    }

    private async Task<OrderProofResult> AttachCoreAsync(Guid orderId, Guid companyId, string storageKey, StayPaymentProofService.ReadFile file, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await AdvisoryLock.AcquireAsync(db, $"stay-service-order-proofs:{orderId}");
            var order = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
            if (StayStateMachine.IsTerminal(order.Status) || order.Status == StayBookingStatus.Confirmed) return new OrderProofResult(ProofOutcome.NotAllowed, order);
            var count = await db.StayPaymentProofs.CountAsync(p => p.StayServiceOrderId == orderId, ct);
            if (count >= options.Value.PaymentProofs.MaxPerBooking) return new OrderProofResult(ProofOutcome.LimitReached, order);

            var from = order.Status;
            if (order.Status == StayBookingStatus.Held)
            {
                // The SAME clock value as the task's expiring UPDATE: exactly one of the two touches the row.
                var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "StayServiceOrders" SET "Status" = {(int)StayBookingStatus.AwaitingPaymentCheck}, "HoldExpiresAtUtc" = NULL,
                           "Version" = "Version" + 1, "UpdatedAtUtc" = {now}
                    WHERE "Id" = {orderId} AND "Status" = {(int)StayBookingStatus.Held} AND "HoldExpiresAtUtc" > {now}
                    """, ct);
                if (rows == 0) goto expired;
            }
            else
            {
                var touched = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "StayServiceOrders" SET "Version" = "Version" + 1, "UpdatedAtUtc" = {now} WHERE "Id" = {orderId} AND "Status" = {(int)StayBookingStatus.AwaitingPaymentCheck}""", ct);
                if (touched == 0) return new OrderProofResult(ProofOutcome.NotAllowed, await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct));
            }

            db.StayPaymentProofs.Add(new StayPaymentProof
            {
                Id = Guid.NewGuid(), StayServiceOrderId = orderId, CompanyId = companyId, StorageKey = storageKey, ContentType = file.ContentType,
                SizeBytes = file.Bytes.Length, UploadedAtUtc = now,
            });
            var fresh = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
            await eventLog.AppendAsync(fresh, StayServiceOrderEventKind.PaymentProofUploaded, new StayActor(StayActorKind.Guest, null, fresh.GuestName ?? "Гость"),
                from, StayBookingStatus.AwaitingPaymentCheck, detailsJson: $"{{\"proofNumber\":{count + 1}}}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new OrderProofResult(ProofOutcome.Ok, fresh);
        }

    expired:
        // The timer ran out first (the task has not reached the order yet): finish the expiry now so the answer carries the real status.
        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await transitions.ExpireAsync(orderId, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        var after = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        return new OrderProofResult(ProofOutcome.HoldExpired, after);
    }

    /// <summary>The file of a proof of THIS order, or null (not found, purged by retention, or the file is gone).</summary>
    public async Task<(Stream Stream, string ContentType)?> OpenAsync(Guid orderId, Guid proofId, CancellationToken ct = default)
    {
        var proof = await db.StayPaymentProofs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proofId && p.StayServiceOrderId == orderId, ct);
        if (proof?.StorageKey is null || proof.PurgedAtUtc is not null) return null;
        try { return (storage.OpenPrivate(proof.StorageKey), proof.ContentType); }
        catch (FileNotFoundException) { return null; }
    }
}
