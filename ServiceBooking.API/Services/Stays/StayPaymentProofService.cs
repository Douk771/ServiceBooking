using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public enum ProofOutcome { Ok, NotFound, BadFile, NotAllowed, LimitReached, HoldExpired }

public sealed record ProofResult(ProofOutcome Outcome, StayBooking? Booking = null, string? Error = null);

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.8, §37.5.3 — payment proofs. The file is checked BY ITS BYTES (PDF %PDF-, JPEG, PNG, WebP; HEIC is not accepted — iOS Safari converts it
/// to JPEG itself when the form's accept list omits it), images are re-encoded (this removes EXIF/GPS), a PDF is stored as is and never parsed. Files live in the
/// PRIVATE storage and have no URL. The hold-expiry race is decided by ONE conditional UPDATE using the same clock as the task: exactly one outcome.
/// </summary>
public class StayPaymentProofService(
    AppDbContext db, FileStorage storage, HouseOccupancyWriter occupancy, StayBookingEventLog eventLog, StayBookingTransitionService transitions,
    IStaysClock clock, IOptions<StaysOptions> options)
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    public const string NoFile = "Выберите файл";
    public const string TooBig = "Файл больше 10 МБ";
    public const string WrongType = "Можно приложить PDF, JPEG, PNG или WebP";
    public const string Corrupt = "Файл повреждён или это не изображение.";
    public const string NoSpace = "На сервере закончилось место. Попробуйте позже.";

    public sealed record ReadFile(byte[] Bytes, string ContentType, string Extension);

    /// <summary>Validates and prepares the upload; null + error text on refusal. Nothing is written to the disk here.</summary>
    public async Task<(ReadFile? File, string? Error)> ReadAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return (null, NoFile);
        if (file.Length > options.Value.PaymentProofs.MaxFileBytes) return (null, TooBig);
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }
        if (!storage.HasFreeSpace(bytes.LongLength)) return (null, NoSpace);

        if (bytes.Length >= PdfMagic.Length && bytes.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic))
            return (new ReadFile(bytes, "application/pdf", ".pdf"), null);
        if (!ImageSignature.TryDetect(bytes, out var kind)) return (null, WrongType);
        try
        {
            var image = ImageProcessor.Process(bytes, kind, ImageProfile.PaymentProof);
            return (new ReadFile(image.Bytes, image.ContentType, image.Extension), null);
        }
        catch (ImageTooLargeException) { return (null, "Слишком большое изображение — попробуйте файл меньшего размера."); }
        catch (InvalidImageException) { return (null, Corrupt); }
    }

    public async Task<ProofResult> AttachAsync(string token, IFormFile? upload, CancellationToken ct = default)
    {
        var found = await db.StayBookings.AsNoTracking().Where(b => b.PublicToken == token).Select(b => new { b.Id, b.CompanyId, b.HouseId }).FirstOrDefaultAsync(ct);
        if (found is null) return new ProofResult(ProofOutcome.NotFound);

        var (file, error) = await ReadAsync(upload);
        if (file is null) return new ProofResult(ProofOutcome.BadFile, Error: error);

        var key = await storage.SavePrivateAsync(found.CompanyId, file.Bytes, file.Extension);
        try
        {
            var result = await AttachCoreAsync(found.Id, found.CompanyId, found.HouseId, key, file, ct);
            if (result.Outcome != ProofOutcome.Ok) storage.DeletePrivate(key);
            return result;
        }
        catch
        {
            storage.DeletePrivate(key);
            throw;
        }
    }

    private async Task<ProofResult> AttachCoreAsync(Guid bookingId, Guid companyId, Guid houseId, string storageKey, ReadFile file, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await AdvisoryLock.AcquireAsync(db, $"stay-booking-proofs:{bookingId}");
            var booking = await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct);
            if (StayStateMachine.IsTerminal(booking.Status) || booking.Status == StayBookingStatus.Confirmed)
                return new ProofResult(ProofOutcome.NotAllowed, booking);
            var count = await db.StayPaymentProofs.CountAsync(p => p.StayBookingId == bookingId, ct);
            if (count >= options.Value.PaymentProofs.MaxPerBooking) return new ProofResult(ProofOutcome.LimitReached, booking);

            var from = booking.Status;
            if (booking.Status == StayBookingStatus.Held)
            {
                // §37.5.3: the SAME clock value as the task's expiring UPDATE; exactly one of the two touches the row.
                var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "StayBookings" SET "Status" = {(int)StayBookingStatus.AwaitingPaymentCheck}, "HoldExpiresAtUtc" = NULL,
                           "Version" = "Version" + 1, "UpdatedAtUtc" = {now}
                    WHERE "Id" = {bookingId} AND "Status" = {(int)StayBookingStatus.Held} AND "HoldExpiresAtUtc" > {now}
                    """, ct);
                if (rows == 0) goto expired;
                await occupancy.ClearHoldAsync(bookingId);
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "StayBookings" SET "Version" = "Version" + 1, "UpdatedAtUtc" = {now} WHERE "Id" = {bookingId}""", ct);
            }

            db.StayPaymentProofs.Add(new StayPaymentProof
            {
                Id = Guid.NewGuid(), StayBookingId = bookingId, CompanyId = companyId, StorageKey = storageKey, ContentType = file.ContentType,
                SizeBytes = file.Bytes.Length, UploadedAtUtc = now,
            });
            var fresh = await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct);
            await eventLog.AppendAsync(fresh, StayBookingEventKind.PaymentProofUploaded, new StayActor(StayActorKind.Guest, null, fresh.GuestName ?? "Гость"),
                from, StayBookingStatus.AwaitingPaymentCheck, detailsJson: $"{{\"proofNumber\":{count + 1}}}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ProofResult(ProofOutcome.Ok, fresh);
        }

    expired:
        // The timer ran out first (the task has not reached the booking yet): finish the expiry now so the answer carries the real status.
        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await occupancy.LockHouseAsync(houseId);
            await transitions.ExpireAsync(bookingId, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        var after = await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct);
        return new ProofResult(ProofOutcome.HoldExpired, after);
    }

    /// <summary>The file of a proof of THIS booking, or null (not found, purged by retention, or the file is gone).</summary>
    public async Task<(Stream Stream, string ContentType)?> OpenAsync(Guid bookingId, Guid proofId, CancellationToken ct = default)
    {
        var proof = await db.StayPaymentProofs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proofId && p.StayBookingId == bookingId, ct);
        if (proof?.StorageKey is null || proof.PurgedAtUtc is not null) return null;
        try { return (storage.OpenPrivate(proof.StorageKey), proof.ContentType); }
        catch (FileNotFoundException) { return null; }
    }
}
