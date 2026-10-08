using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.7.3 — the timed messages of a booking, once each (lane `main`, every minute): (a) "10 minutes left" for a hold with no proof;
/// (b) the reminder the evening before check-in; (c) the check-in information. Once-only is a mark column set by a conditional UPDATE in the same transaction
/// that queues the message. A missed term (the server was down) is sent on the first pass if it is still relevant, otherwise only marked.
/// </summary>
public sealed class StaysScheduledMessagesTask(
    AppDbContext db, StayNotificationPlanner planner, CheckInInfoReleaser checkInInfo, IStaysClock clock, ILogger<StaysScheduledMessagesTask> logger) : IScheduledTask
{
    public const int BatchSize = 100;
    public static readonly TimeSpan HoldWarning = TimeSpan.FromMinutes(10);
    public static readonly TimeOnly ReminderTime = new(18, 0);

    public string Name => "stays-scheduled-messages";
    public TimeSpan DefaultPeriod => TimeSpan.FromSeconds(60);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var holdWarned = await HoldExpiringAsync(now, ct);
        var reminded = await ArrivalRemindersAsync(now, ct);
        var released = await CheckInInfoAsync(ct);
        var summary = $"hold warnings {holdWarned}, reminders {reminded}, check-in info {released}";
        logger.LogInformation("stays-scheduled-messages: {Summary}", summary);
        return new ScheduledTaskOutcome(holdWarned + reminded + released, holdWarned + reminded + released, 0, summary);
    }

    private async Task<int> HoldExpiringAsync(DateTime now, CancellationToken ct)
    {
        var due = await db.StayBookings.AsNoTracking()
            .Where(b => b.Status == StayBookingStatus.Held && b.HoldReminderQueuedAtUtc == null && b.HoldExpiresAtUtc != null && b.HoldExpiresAtUtc > now &&
                        b.HoldExpiresAtUtc <= now.AddMinutes(10) && !b.PaymentProofs.Any())
            .OrderBy(b => b.HoldExpiresAtUtc).Take(BatchSize).Select(b => b.Id).ToListAsync(ct);
        var count = 0;
        foreach (var id in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StayBookings" SET "HoldReminderQueuedAtUtc" = {now} WHERE "Id" = {id} AND "HoldReminderQueuedAtUtc" IS NULL AND "Status" = {(int)StayBookingStatus.Held}""", ct);
            if (rows == 1)
            {
                var booking = await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == id, ct);
                // the hold must have been long enough for the warning to make sense (HoldMinutes >= 20)
                var holdMinutes = await db.StaysSettings.AsNoTracking().Where(s => s.CompanyId == booking.CompanyId).Select(s => (int?)s.HoldMinutes).FirstOrDefaultAsync(ct) ?? 30;
                if (holdMinutes >= 20) await planner.OnScheduledAsync(booking, StayScheduledKind.HoldExpiring, ct);
                await db.SaveChangesAsync(ct);
                count++;
            }
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return count;
    }

    private async Task<int> ArrivalRemindersAsync(DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var candidates = await db.StayBookings.AsNoTracking()
            .Where(b => b.Status == StayBookingStatus.Confirmed && b.ArrivalReminderQueuedAtUtc == null && b.CheckInDate >= today.AddDays(-1) && b.CheckInDate <= today.AddDays(2))
            .OrderBy(b => b.CheckInDate).Take(BatchSize * 4).ToListAsync(ct);
        var count = 0;
        foreach (var b in candidates)
        {
            var reminderAt = StayTime.ToUtc(b.TimeZoneIdSnapshot, b.CheckInDate.AddDays(-1), ReminderTime);
            var checkInAt = StayTime.ToUtc(b.TimeZoneIdSnapshot, b.CheckInDate, b.CheckInTimeSnapshot);
            if (now < reminderAt) continue;
            var enabled = await db.StaysSettings.AsNoTracking().Where(s => s.CompanyId == b.CompanyId).Select(s => (bool?)s.ArrivalReminderEnabled).FirstOrDefaultAsync(ct) ?? true;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StayBookings" SET "ArrivalReminderQueuedAtUtc" = {now} WHERE "Id" = {b.Id} AND "ArrivalReminderQueuedAtUtc" IS NULL""", ct);
            if (rows == 1 && enabled && now < checkInAt)
            {
                await planner.OnScheduledAsync(b, StayScheduledKind.ArrivalReminder, ct);
                await db.SaveChangesAsync(ct);
                count++;
            }
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return count;
    }

    private async Task<int> CheckInInfoAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var candidates = await db.StayBookings.AsNoTracking()
            .Where(b => b.Status == StayBookingStatus.Confirmed && b.CheckInInfoReleasedAtUtc == null && b.CheckInDate <= today.AddDays(1) && b.CheckOutDate >= today.AddDays(-1))
            .OrderBy(b => b.CheckInDate).Take(BatchSize * 4).ToListAsync(ct);
        var count = 0;
        foreach (var b in candidates)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await checkInInfo.ReleaseIfDueAsync(b, StayActor.System, ct)) { await db.SaveChangesAsync(ct); count++; }
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return count;
    }
}
