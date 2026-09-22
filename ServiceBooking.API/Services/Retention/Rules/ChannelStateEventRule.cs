using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>T5-B8/B9. Deletes a <see cref="Core.Entities.ChannelStateEvent"/> older than
/// <see cref="RetentionPeriods.ChannelStateEventDays"/> — pure technical diagnostics (§13.5), no personal
/// data.</summary>
public sealed class ChannelStateEventRule(AppDbContext db) : IRetentionRule
{
    public string Name => "channel-state-event";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).ChannelStateEvent;

        IQueryable<Core.Entities.ChannelStateEvent> Query(Guid cursor) => db.ChannelStateEvents
            .Where(e => e.Id > cursor && e.OccurredAtUtc < cutoff)
            .OrderBy(e => e.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, e => e.Id,
            mutate: e => db.ChannelStateEvents.Remove(e),
            ctx, db, ct,
            dateOf: e => e.OccurredAtUtc);
    }
}
