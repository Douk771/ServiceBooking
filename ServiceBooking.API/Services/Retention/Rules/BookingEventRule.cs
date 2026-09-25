using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE10.md §107 (Q2). Registered and run like every other rule, but the retention period
/// for <see cref="BookingEvent"/> has no legal answer yet — <see cref="RetentionPeriods.BookingEventDays"/>
/// defaults to 0, which this rule reads as "срок не задан", not "delete everything older than the epoch".
/// At 0 the rule deletes NOTHING and says so honestly in the summary line, instead of either silently
/// no-opping (which would hide that retention for this table isn't configured at all) or deleting on a
/// guessed cutoff nobody signed off on. When legal-counsel gives a number, changing
/// <c>Retention:BookingEventDays</c> in configuration is the entire fix — no migration, no redeploy of
/// code, no touching this class.
///
/// Deliberately NOT a startup fail-fast (unlike <see cref="DeploymentSafetyChecks.ValidateRetentionPeriods"/>'s
/// two checks for <c>TemplateHistoryDays</c>/<c>ConsentRecordDays</c>): those guard KNOWN legal minimums;
/// here the minimum itself is the open question, and refusing to start the app over an unanswered legal
/// question would block the whole cycle's release on someone else's timeline, which the spec does not ask
/// for ("blocks the retention CONFIGURATION, not the table itself").
/// </summary>
public sealed class BookingEventRule(AppDbContext db) : IRetentionRule
{
    public string Name => "booking-event";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        if (ctx.Periods.BookingEventDays <= 0)
        {
            var skippedSummary = $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: " +
                "срок хранения не настроен, правило пропущено";
            // TD-04 (ARCHITECTURE_CYCLE16.md §246.3): Skipped=true — "not configured", distinct from
            // "configured and had nothing to do" (Scanned: 0 alone would look identical to that).
            return new RetentionOutcome(Name, Scanned: 0, Affected: 0, skippedSummary) { Skipped = true };
        }

        var cutoff = ctx.NowUtc.AddDays(-ctx.Periods.BookingEventDays);

        IQueryable<BookingEvent> Query(Guid cursor) => db.BookingEvents
            .Where(e => e.Id > cursor && e.OccurredAtUtc < cutoff)
            .OrderBy(e => e.Id);

        var cursorId = Guid.Empty;
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

            db.BookingEvents.RemoveRange(batch);

            if (!ctx.DryRun)
                await db.SaveChangesAsync(ct);

            // Cleared every batch, real mode included — same reasoning as ClientNotePhotoRule's fix
            // for RetentionRuleRunner's tracker growth on a long real-mode run.
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
