using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.7.4 — releases the check-in information of a booking when <see cref="CheckInInfoRelease"/> says it is due: the mark
/// <c>CheckInInfoReleasedAtUtc</c> (conditional UPDATE — once), the journal event (which plans the guest's message). Callers: the scheduled task, the payment
/// confirmation, the creation of a booking with no prepayment.
/// </summary>
public class CheckInInfoReleaser(AppDbContext db, StayBookingEventLog eventLog, IStaysClock clock)
{
    public async Task<bool> ReleaseIfDueAsync(StayBooking booking, StayActor actor, CancellationToken ct = default)
    {
        if (booking.CheckInInfoReleasedAtUtc is not null) return false;
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == booking.CompanyId, ct) ?? new StaysSettings();
        var houseText = await db.Houses.AsNoTracking().Where(h => h.Id == booking.HouseId).Select(h => h.CheckInInfoText).FirstOrDefaultAsync(ct);
        var now = clock.UtcNow;
        var hasText = !string.IsNullOrWhiteSpace(settings.CheckInInfoText) || !string.IsNullOrWhiteSpace(houseText);
        if (!CheckInInfoRelease.IsDue(booking.Status, hasText, alreadyReleased: false, booking.TimeZoneIdSnapshot, now,
                booking.CheckInDate, settings.CheckInInfoSendTime, booking.CheckOutDate, booking.CheckOutTimeSnapshot)) return false;

        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "StayBookings" SET "CheckInInfoReleasedAtUtc" = {now} WHERE "Id" = {booking.Id} AND "CheckInInfoReleasedAtUtc" IS NULL""", ct);
        if (rows == 0) return false;
        booking.CheckInInfoReleasedAtUtc = now;
        db.Entry(booking).Property(b => b.CheckInInfoReleasedAtUtc).IsModified = false;
        await eventLog.AppendAsync(booking, StayBookingEventKind.CheckInInfoReleased, actor, booking.Status, booking.Status);
        return true;
    }
}
