using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9. A legal BACKSTOP cap, independent of <see cref="Scheduling.Tasks.PhotoRetentionCleanupTask"/>'s
/// per-company tariff window: that task already deletes photos on a schedule, but its cutoff comes from
/// each company's <c>PhotoRetention</c> plan setting, not from law. This rule deletes any
/// <see cref="ClientNotePhoto"/> older than <see cref="RetentionPeriods.ClientNotePhotoDays"/> regardless of
/// tariff — so a future plan misconfiguration (or a plan added later with a longer window) can never push a
/// photo past what LEGAL_REVIEW.md §13.5 calls the legal maximum (ч. 7 ст. 5: "«бессрочно» не может быть
/// сроком хранения"). Today, with only SixMonths/TwelveMonths tariffs and a 365-day default here, this rule
/// rarely fires — that overlap is expected and harmless (deleting an already-deleted row is a no-op), not a
/// sign of redundant code.
/// </summary>
public sealed class ClientNotePhotoRule(AppDbContext db, FileStorage storage) : IRetentionRule
{
    public string Name => "client-note-photo";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).ClientNotePhoto;

        IQueryable<ClientNotePhoto> Query(Guid cursor) => db.ClientNotePhotos
            .Where(p => p.Id > cursor && p.CreatedAt < cutoff)
            .OrderBy(p => p.Id);

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
            foreach (var photo in batch)
                if (oldest is null || photo.CreatedAt < oldest) oldest = photo.CreatedAt;

            var paths = batch.Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();
            db.ClientNotePhotos.RemoveRange(batch);

            if (!ctx.DryRun)
            {
                await db.SaveChangesAsync(ct);
                foreach (var (full, thumb) in paths)
                {
                    storage.DeletePrivate(full);
                    storage.DeletePrivate(thumb);
                }
            }
            else
            {
                db.ChangeTracker.Clear();
            }

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
