using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.6 (US-20-03, О-3). Deletes a <see cref="PlatformNotice"/> once it has
/// been out of its own visibility window (<see cref="PlatformNotice.VisibleUntilUtc"/>, NOT
/// <c>PublishedAtUtc</c> — a notice already carries the "show it for at least this long" promise in that
/// field, computed once at publish time) for longer than <see cref="RetentionPeriods.PlatformNoticeDays"/>
/// (default 1095 — SPEC's "как у согласий" assumption). Its
/// <see cref="Core.Entities.PlatformNoticeAcknowledgement"/> rows cascade-delete with it at the database
/// level (AppDbContext's FK configuration) — no separate rule needed for them.
/// </summary>
public sealed class PlatformNoticeRule(AppDbContext db) : IRetentionRule
{
    public string Name => "platform-notice";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = ctx.NowUtc.AddDays(-ctx.Periods.PlatformNoticeDays);

        IQueryable<PlatformNotice> Query(Guid cursor) => db.PlatformNotices
            .Where(n => n.Id > cursor && n.VisibleUntilUtc < cutoff)
            .OrderBy(n => n.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, n => n.Id,
            mutate: n => db.PlatformNotices.Remove(n),
            ctx, db, ct,
            dateOf: n => n.VisibleUntilUtc);
    }
}
