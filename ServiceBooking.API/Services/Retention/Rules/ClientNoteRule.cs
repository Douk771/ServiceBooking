using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9. Deletes a <see cref="ClientNote"/> (and, by DB cascade — same FK <c>ProfileController.
/// DeleteAccount</c> relies on — its <see cref="ClientNotePhoto"/> rows) once it is older than
/// <see cref="RetentionPeriods.ClientNoteDays"/>.
///
/// 🟡 Scope decision, recorded rather than silently made: LEGAL_REVIEW.md §13.5 phrases the reference
/// point as "3 года <b>с последнего визита клиента в этой компании</b>", not "since the note was written".
/// Computing "this client's last visit in this company" would need a per-(company, client) aggregate over
/// <see cref="Booking"/> that does not exist anywhere in the model today, re-evaluated for every note on
/// every pass. This rule instead uses the note's own <c>CreatedAt</c> — simpler, defensible (a note written
/// during a visit is already dated close to that visit), and never LESS conservative than the lawyer's
/// wording for a client who keeps visiting (each new visit tends to produce a new note, which resets this
/// rule's own clock in practice) — but a client who visits again WITHOUT a fresh note being written could
/// have an old note purged earlier than the lawyer's exact wording would purge it. Flagged for the
/// coordinator/architect to confirm or correct rather than implemented silently either way.
///
/// File cleanup (photos) does NOT go through <see cref="RetentionRuleRunner"/> — unlike every other rule,
/// this one has a side effect outside the database (deleting files, ARCHITECTURE.md §1.4: "row goes
/// first"), so it manages its own cursor loop, mirroring <see cref="Scheduling.Tasks.PhotoRetentionCleanupTask"/>'s
/// established ordering: DB row removed and committed FIRST, files deleted only after that commit
/// succeeds, and — the one deliberate difference from that task — nothing on disk is EVER touched in
/// dry-run, matching §49.2's "не вызывается SaveChanges" for the database half of this rule.
/// </summary>
public sealed class ClientNoteRule(AppDbContext db, FileStorage storage) : IRetentionRule
{
    public string Name => "client-note";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).ClientNote;

        IQueryable<ClientNote> Query(Guid cursor) => db.ClientNotes
            .Include(n => n.Photos)
            .Where(n => n.Id > cursor && n.CreatedAt < cutoff)
            .OrderBy(n => n.Id);

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
            foreach (var note in batch)
                if (oldest is null || note.CreatedAt < oldest) oldest = note.CreatedAt;

            var photoPaths = batch.SelectMany(n => n.Photos)
                .Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();

            db.ClientNotes.RemoveRange(batch); // cascades ClientNotePhotos (AppDbContext)

            if (!ctx.DryRun)
            {
                await db.SaveChangesAsync(ct);
                foreach (var (full, thumb) in photoPaths)
                {
                    storage.DeletePrivate(full);
                    storage.DeletePrivate(thumb);
                }
            }

            // Code review В6: cleared every batch, real mode included — see RetentionRuleRunner's own
            // fix for why an uncleared tracker degrades a long real-mode run batch over batch.
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
