using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Slots;
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
    AppDbContext db, StayNotificationPlanner planner, CheckInInfoReleaser checkInInfo, IStaysClock clock, ArrivalReminderService reminders, StayBookingEventLog eventLog, StayServiceOrderEventLog orderEventLog,
    ILogger<StaysScheduledMessagesTask> logger) : IScheduledTask
{
    public const int BatchSize = 100;
    public static readonly TimeSpan HoldWarning = TimeSpan.FromMinutes(10);
    /// <summary>The default time of the reminder (cycle 37); since cycle 39 the company's own <c>ArrivalReminderTime</c> is used.</summary>
    public static readonly TimeOnly ReminderTime = new(18, 0);

    public string Name => "stays-scheduled-messages";
    public TimeSpan DefaultPeriod => TimeSpan.FromSeconds(60);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var holdWarned = await HoldExpiringAsync(now, ct);
        var reminded = await ArrivalRemindersAsync(now, ct);
        var released = await CheckInInfoAsync(ct);
        var orderWarned = await OrderHoldExpiringAsync(now, ct);
        var sessionReminded = await SessionRemindersAsync(now, ct);
        var summary = $"hold warnings {holdWarned}, order hold warnings {orderWarned}, reminders {reminded}, check-in info {released}, session reminders {sessionReminded}";
        logger.LogInformation("stays-scheduled-messages: {Summary}", summary);
        var total = holdWarned + orderWarned + reminded + released + sessionReminded;
        return new ScheduledTaskOutcome(total, total, 0, summary);
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

    /// <summary>ARCHITECTURE_CYCLE39.md §39.7.7 (а′) — «10 minutes left» for a held ORDER of a service, the same rules as for a booking.</summary>
    private async Task<int> OrderHoldExpiringAsync(DateTime now, CancellationToken ct)
    {
        var due = await db.StayServiceOrders.AsNoTracking()
            .Where(o => o.Status == StayBookingStatus.Held && o.HoldReminderQueuedAtUtc == null && o.HoldExpiresAtUtc != null && o.HoldExpiresAtUtc > now &&
                        o.HoldExpiresAtUtc <= now.AddMinutes(10) && !o.PaymentProofs.Any())
            .OrderBy(o => o.HoldExpiresAtUtc).Take(BatchSize).Select(o => o.Id).ToListAsync(ct);
        var count = 0;
        foreach (var id in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StayServiceOrders" SET "HoldReminderQueuedAtUtc" = {now} WHERE "Id" = {id} AND "HoldReminderQueuedAtUtc" IS NULL AND "Status" = {(int)StayBookingStatus.Held}""", ct);
            if (rows == 1)
            {
                var order = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == id, ct);
                var holdMinutes = await db.StaysSettings.AsNoTracking().Where(s => s.CompanyId == order.CompanyId).Select(s => (int?)s.HoldMinutes).FirstOrDefaultAsync(ct) ?? 30;
                if (holdMinutes >= 20) await planner.OnOrderScheduledAsync(order, ct);
                await db.SaveChangesAsync(ct);
                count++;
            }
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return count;
    }

    /// <summary>The look-ahead of the third pass: the earliest reminder moment is 24 h (the largest setting) before the start, plus an hour of slack for the shift out of the night.</summary>
    public static readonly TimeSpan SessionReminderHorizon = TimeSpan.FromHours(25);
    public const int SessionReminderBatchSize = 50;
    /// <summary>Wait and Skip write nothing, so the pass walks the whole window page by page (by start, then id) instead of re-reading the same first page; the cap keeps one pass bounded.</summary>
    public const int SessionReminderMaxPages = 40;

    /// <summary>
    /// ARCHITECTURE_CYCLE42.md §42.9.5 — the third pass: a confirmed booking of a company with <c>ServiceReminderHours</c> whose session starts within 25 hours is judged by the pure
    /// <see cref="SessionReminderPolicy"/> in the company's local time. <c>Send</c>: a conditional UPDATE of <c>SessionReminderAtUtc</c> (once only; set even when no channel is available),
    /// the event <c>SessionReminderSent</c> (journal + revision) and the notifications through the planner — all in one transaction. <c>Wait</c> and <c>Skip</c> write nothing.
    /// </summary>
    private async Task<int> SessionRemindersAsync(DateTime now, CancellationToken ct)
    {
        var horizon = now + SessionReminderHorizon;
        var count = 0;
        // Rows that were judged Wait/Skip stay in the selection (nothing is written), rows that were sent leave it: the next page starts after the ones that stayed.
        var stayed = 0;
        for (var page = 0; page < SessionReminderMaxPages; page++)
        {
            var query = from o in db.StayServiceOrders.AsNoTracking()
                        join s in db.StayServiceSessions.AsNoTracking() on o.Id equals s.StayServiceOrderId
                        join st in db.StaysSettings.AsNoTracking() on o.CompanyId equals st.CompanyId
                        where o.Status == StayBookingStatus.Confirmed && o.SessionReminderAtUtc == null && st.ServiceReminderHours != null &&
                              s.ReleasedAtUtc == null && s.StartUtc > now && s.StartUtc <= horizon
                        select new { o.Id, o.CreatedAtUtc, o.TimeZoneIdSnapshot, s.StartUtc, Hours = st.ServiceReminderHours!.Value };
            var batch = await query.OrderBy(x => x.StartUtc).ThenBy(x => x.Id).Skip(stayed).Take(SessionReminderBatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var c in batch)
            {
                var startLocal = StayTime.LocalDateTime(c.TimeZoneIdSnapshot, c.StartUtc);
                var moment = SessionReminderPolicy.Moment(startLocal, c.Hours);
                var decision = SessionReminderPolicy.Decide(moment, StayTime.LocalDateTime(c.TimeZoneIdSnapshot, c.CreatedAtUtc), StayTime.LocalDateTime(c.TimeZoneIdSnapshot, now), startLocal);
                if (decision == ReminderDecision.Send && await SendSessionReminderAsync(c.Id, now, ct)) count++;
                else stayed++;
            }
            if (batch.Count < SessionReminderBatchSize) break;
        }
        return count;
    }

    private async Task<bool> SendSessionReminderAsync(Guid orderId, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "StayServiceOrders" SET "SessionReminderAtUtc" = {now} WHERE "Id" = {orderId} AND "SessionReminderAtUtc" IS NULL AND "Status" = {(int)StayBookingStatus.Confirmed}""", ct);
        if (rows == 1)
        {
            var order = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
            await orderEventLog.AppendAsync(order, StayServiceOrderEventKind.SessionReminderSent, StayActor.System, order.Status, order.Status);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return rows == 1;
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
            // The CURRENT time of the setting on every pass: a new time applies to every reminder not sent yet (ARCHITECTURE_CYCLE39.md §39.11.6).
            var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == b.CompanyId, ct);
            var reminderAt = StayTime.ToUtc(b.TimeZoneIdSnapshot, b.CheckInDate.AddDays(-1), settings?.ArrivalReminderTime ?? ReminderTime);
            var checkInAt = StayTime.ToUtc(b.TimeZoneIdSnapshot, b.CheckInDate, b.CheckInTimeSnapshot);
            if (now < reminderAt) continue;
            var enabled = settings?.ArrivalReminderEnabled ?? true;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StayBookings" SET "ArrivalReminderQueuedAtUtc" = {now} WHERE "Id" = {b.Id} AND "ArrivalReminderQueuedAtUtc" IS NULL""", ct);
            if (rows == 1 && enabled && now < checkInAt)
            {
                // The snapshot of the page text is taken in the SAME transaction, also when no channel is available (Р39-17); later edits of the template change nothing.
                var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == b.CompanyId, ct);
                var facts = await reminders.FactsAsync(b, company, unsubscribeUrl: null, ct);
                var pageText = ArrivalReminderTemplate.Render(settings?.ArrivalReminderTemplate, facts, ReminderMode.Page).Text;
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "StayBookings" SET "ArrivalReminderPageText" = {pageText}, "ArrivalReminderSentAtUtc" = {now} WHERE "Id" = {b.Id}""", ct);
                await eventLog.AppendAsync(b, StayBookingEventKind.ArrivalReminderSent, StayActor.System, b.Status, b.Status);
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
