using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>T5-B8/B9. Deletes a <see cref="Core.Entities.ClientHealthNote"/> once it hasn't been updated
/// in <see cref="RetentionPeriods.ClientHealthNoteDays"/> — mirrors <see cref="ClientNoteRule"/>'s
/// reasoning (§13.5: a specialised client note), using <c>UpdatedAt</c> since a health note has no
/// separate creation timestamp (it is upserted in place, ClientConsentsController).</summary>
public sealed class ClientHealthNoteRule(AppDbContext db) : IRetentionRule
{
    public string Name => "client-health-note";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).ClientHealthNote;

        IQueryable<Core.Entities.ClientHealthNote> Query(Guid cursor) => db.ClientHealthNotes
            .Where(h => h.Id > cursor && h.UpdatedAt < cutoff)
            .OrderBy(h => h.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, h => h.Id,
            mutate: h => db.ClientHealthNotes.Remove(h),
            ctx, db, ct,
            dateOf: h => h.UpdatedAt);
    }
}
