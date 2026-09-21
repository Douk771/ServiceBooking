using Microsoft.EntityFrameworkCore;

namespace ServiceBooking.API.Services.Retention;

/// <summary>
/// The one place that decides how a rule turns "which rows" into "scanned/affected" (§49.2, §49.4). Every
/// concrete <see cref="IRetentionRule"/> in this codebase goes through this helper instead of writing its
/// own batching loop, specifically so the coordinator's requirement — dry-run and real mode run the exact
/// same selection query, not a "similar" one — is guaranteed by one piece of shared code instead of by
/// eleven separate promises to remember it.
///
/// <b>How "same query" is achieved:</b> <c>queryFactory</c> is called fresh every batch with
/// only a keyset cursor (<c>Id &gt; lastSeenId</c>) added on top of the rule's own WHERE clause — it never
/// depends on a MUTATED field (e.g. "ContentRedactedAtUtc IS NULL") having actually been persisted. That
/// is what makes dry-run safe to page through at all: if paging instead relied on the mutation already
/// having happened, dry-run (which never calls SaveChanges) would re-select the exact same first batch
/// forever. Real mode and dry-run mode call <c>queryFactory</c> with byte-identical predicates;
/// the ONLY difference is that real mode calls <c>SaveChangesAsync</c> after mutating a batch and dry-run
/// discards the batch's changes instead (<see cref="DbContext.ChangeTracker"/> is cleared, nothing is ever
/// sent to the database) — matching §49.2's "отличие ровно одно — не вызывается SaveChanges".
/// </summary>
public static class RetentionRuleRunner
{
    public static async Task<RetentionOutcome> RunAsync<T>(
        string ruleName,
        Func<Guid, IQueryable<T>> queryFactory,
        Func<T, Guid> idOf,
        Action<T> mutate,
        RetentionContext ctx,
        DbContext db,
        CancellationToken ct,
        Func<T, DateTime>? dateOf = null) where T : class
    {
        var cursor = Guid.Empty;
        var scanned = 0;
        var affected = 0;
        DateTime? oldest = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await queryFactory(cursor).Take(ctx.BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            scanned += batch.Count;
            if (dateOf is not null)
            {
                foreach (var item in batch)
                {
                    var date = dateOf(item);
                    if (oldest is null || date < oldest) oldest = date;
                }
            }
            foreach (var item in batch) mutate(item);

            if (!ctx.DryRun)
            {
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Nothing is sent to the database at all in dry-run — the mutated in-memory values are
                // simply discarded. This is the one and only branch point between the two modes.
                db.ChangeTracker.Clear();
            }

            affected += batch.Count;
            cursor = idOf(batch[^1]);

            if (batch.Count < ctx.BatchSize) break; // last (partial) page
        }

        var summary = oldest is null
            ? $"retention[{(ctx.DryRun ? "dry" : "live")}] {ruleName}: scanned={scanned} affected={affected}"
            : $"retention[{(ctx.DryRun ? "dry" : "live")}] {ruleName}: scanned={scanned} affected={affected} oldest={oldest:yyyy-MM-dd}";
        return new RetentionOutcome(ruleName, scanned, affected, summary);
    }
}
