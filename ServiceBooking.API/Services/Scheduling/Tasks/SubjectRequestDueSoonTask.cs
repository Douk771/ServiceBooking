using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Signals;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// TD-03-quater, second half (SPEC_CYCLE16_TECH_DEBT.md — "заодно, тем же каналом: сигнал о приближении
/// срока... по обращениям в статусе, отличном от Answered/Rejected"). One GlitchTip signal per subject
/// request, sent once, one working day before <c>DueAtUtc</c>.
///
/// 🔴 No migration this cycle (ARCHITECTURE_CYCLE16.md §240.3) means <see cref="Core.Entities.SubjectRequest"/>
/// has no "already warned" column to make this idempotent the conventional way. Idempotency instead comes
/// from running once a day and matching on a day-granularity window: a request is "due soon" on the one
/// calendar day whose "one working day later" lands on its due date. As long as this task's own daily
/// pass actually runs that day, each request is matched on exactly one pass. A missed pass (task disabled,
/// prolonged outage spanning the whole matching day) means that request's due-soon signal is silently
/// skipped rather than sent late or duplicated — accepted as the cheaper failure mode of the two, and
/// called out in the cycle report rather than solved with a migration.
/// </summary>
public sealed class SubjectRequestDueSoonTask(
    AppDbContext db,
    INotificationClock clock,
    IGlitchTipSignalService signals,
    ILogger<SubjectRequestDueSoonTask> logger) : IScheduledTask
{
    public string Name => "subject-request-due-soon";
    public TimeSpan DefaultPeriod => TimeSpan.FromDays(1);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        // 🔴 Fixed by code review (cycle 16): WorkingDays.Add is NOT injective going forward — Add from
        // Friday, Saturday AND Sunday all land on the same following Monday, so a naive "today+1 working
        // day == due date" match fired three times for any Monday-due request. The correct inverse of
        // "one working day before DueAtUtc" is WorkingDays.PreviousWorkingDay(DueAtUtc) — a function that
        // maps every due date to exactly ONE calendar day. Since that day is always itself a working day
        // (PreviousWorkingDay never returns a Saturday/Sunday), it's enough to also require that TODAY is
        // a working day before doing the (still forward, but now safe) date-only DB comparison below —
        // on a weekend run this task simply has no candidates, which is correct: the Friday run already
        // matched the one request due the following Monday.
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            const string weekendSummary = "subject-request-due-soon: 0 обращение(й) с приближающимся сроком (выходной)";
            logger.LogInformation("{Summary}", weekendSummary);
            return new ScheduledTaskOutcome(0, 0, 0, weekendSummary);
        }

        // "One working day before DueAtUtc" read the other way round: a request is due-soon TODAY when
        // adding one working day to today's date lands on the request's due date. Safe now that today is
        // known to be a working day (see guard above) — WorkingDays.PreviousWorkingDay(dueSoonDate) is
        // guaranteed to equal `now.Date` and no other calendar day.
        var dueSoonDate = WorkingDays.Add(now, 1).Date;

        // 🔴 Fixed by code review (cycle 16): the spec's own condition is a BLACKLIST ("в статусе,
        // отличном от Answered/Rejected"), not a whitelist of the two statuses that happen to exist
        // today. Today's 4-value SubjectRequestStatus makes the two forms equivalent, but a whitelist
        // silently drops any future status from due-soon signals instead of including it by default.
        // 🔴 Fixed by code review (cycle 16): `r.DueAtUtc.Date == dueSoonDate` translates to
        // `date_trunc('day', ...)` on the Postgres side, whose result for a timestamptz column depends
        // on the session's TimeZone GUC even though the parameter itself is a UTC midnight — on a
        // non-UTC server this either never matches (silent, indistinguishable from a legitimate zero) or
        // fails to translate at all. A half-open range is timezone-independent and sargable against the
        // (Status, DueAtUtc) index.
        var dueSoonRangeEndExclusive = dueSoonDate.AddDays(1);
        var candidates = await db.SubjectRequests
            .Where(r => r.Status != SubjectRequestStatus.Answered && r.Status != SubjectRequestStatus.Rejected)
            .Where(r => r.DueAtUtc >= dueSoonDate && r.DueAtUtc < dueSoonRangeEndExclusive)
            .Select(r => new { r.Reference, r.Kind, r.DueAtUtc })
            .ToListAsync(ct);

        var sentCount = 0;
        var failedCount = 0;
        foreach (var request in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // 🔴 Composition fixed by legal review §6: kind, reference, due date — nothing else (no
            // phone, no message text, no counts). Same constraint as the intake signal.
            var message =
                $"Срок ответа приближается: вид={request.Kind}, референс={request.Reference}, " +
                $"срок={request.DueAtUtc:yyyy-MM-dd}";
            // 🔴 Fixed by code review (cycle 16): SendAsync swallows every failure internally and used to
            // return void, so this outcome always reported 0 failures regardless of whether GlitchTip was
            // actually reachable. Its bool return is now used to tell a real success from a best-effort
            // failure in the task's own summary.
            if (await signals.SendAsync(message, ct))
                sentCount++;
            else
                failedCount++;
        }

        var summary = failedCount == 0
            ? $"subject-request-due-soon: {candidates.Count} обращение(й) с приближающимся сроком"
            : $"subject-request-due-soon: {candidates.Count} обращение(й) с приближающимся сроком, " +
              $"{failedCount} сигнал(ов) не доставлен(о)";
        logger.LogInformation("{Summary}", summary);
        return new ScheduledTaskOutcome(candidates.Count, sentCount, failedCount, summary);
    }
}
