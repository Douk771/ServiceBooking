using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §406.2 (US-20-05, Т20-06). Deletes a <see cref="GuestDataGateEvent"/> older
/// than <see cref="RetentionPeriods.GuestDataGateEventDays"/> (default 365 — п. 13.2 D1). Not built on
/// <see cref="RetentionRuleRunner"/> — that helper's cursor is hardcoded to <see cref="Guid"/>, and
/// <see cref="GuestDataGateEvent.Id"/> is a <c>bigint identity</c> (§406.2's own reasoning: a journal that
/// grows one row per gate trip, never edited, benefits from a narrow index-friendly key). Manual
/// keyset-paginated loop, mirroring <see cref="BookingEventRule"/>'s shape for the same reason.
/// </summary>
public sealed class GuestDataGateEventRule(AppDbContext db) : IRetentionRule
{
    public string Name => "guest-data-gate-event";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = ctx.NowUtc.AddDays(-ctx.Periods.GuestDataGateEventDays);

        IQueryable<GuestDataGateEvent> Query(long cursor) => db.GuestDataGateEvents
            .Where(e => e.Id > cursor && e.OccurredAtUtc < cutoff)
            .OrderBy(e => e.Id);

        var cursorId = 0L;
        var scanned = 0;
        var affected = 0;
        DateTime? oldest = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await Query(cursorId).Take(ctx.BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            scanned += batch.Count;
            foreach (var evt in batch)
                if (oldest is null || evt.OccurredAtUtc < oldest) oldest = evt.OccurredAtUtc;

            db.GuestDataGateEvents.RemoveRange(batch);

            if (!ctx.DryRun)
                await db.SaveChangesAsync(ct);

            db.ChangeTracker.Clear();

            affected += batch.Count;
            cursorId = batch[^1].Id;

            if (batch.Count < ctx.BatchSize) break;
        }

        var mode = ctx.DryRun ? "dry" : "live";
        var summary = oldest is null
            ? $"retention[{mode}] {Name}: scanned={scanned} affected={affected}"
            : $"retention[{mode}] {Name}: scanned={scanned} affected={affected} oldest={oldest:yyyy-MM-dd}";
        return new RetentionOutcome(Name, scanned, affected, summary);
    }
}
