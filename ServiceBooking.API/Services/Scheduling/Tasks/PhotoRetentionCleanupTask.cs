using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// The first (and, for this cycle, only) scheduled task: removes client-note photos past their
/// company's tariff retention window, then sweeps orphaned files (a file on disk with no matching row —
/// the expected leftover of the upload/delete pipeline's "worst case is an orphan" ordering,
/// ARCHITECTURE.md §1.4). US-21 pp.8-12.
/// </summary>
public sealed class PhotoRetentionCleanupTask(
    AppDbContext db, SubscriptionResolver subscriptionResolver, FileStorage storage, ILogger<PhotoRetentionCleanupTask> logger)
    : IScheduledTask
{
    public string Name => "photo-retention-cleanup";
    public TimeSpan DefaultPeriod => TimeSpan.FromDays(1);

    // Short transactions, small batches — keeps this task from competing with uploads/reads for the
    // same index for more than a few milliseconds at a time (ARCHITECTURE.md §9.1).
    private const int ChunkSize = 200;

    // Orphans are only removed once they are older than this — a file the upload pipeline has already
    // written but whose row hasn't committed yet (ARCHITECTURE.md §4.1 steps 10-11) must not be swept
    // out from under a request that is still in flight (risk R3).
    private static readonly TimeSpan OrphanMinAge = TimeSpan.FromHours(24);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var scanned = 0;
        var deleted = 0;
        long bytesFreed = 0;

        var (expiredScanned, expiredDeleted, expiredBytes) = await DeleteExpiredPhotosAsync(ct);
        scanned += expiredScanned;
        deleted += expiredDeleted;
        bytesFreed += expiredBytes;

        var (orphanScanned, orphanDeleted, orphanBytes) = await DeleteOrphanFilesAsync(ct);
        scanned += orphanScanned;
        deleted += orphanDeleted;
        bytesFreed += orphanBytes;

        var summary = $"scanned {scanned}, deleted {deleted}, freed {FormatBytes(bytesFreed)}";
        return new ScheduledTaskOutcome(scanned, deleted, bytesFreed, summary);
    }

    /// <summary>
    /// Groups every company that owns at least one photo into (at most) three retention buckets and
    /// deletes what's expired in each, batching so no single query or transaction runs long
    /// (ARCHITECTURE.md §7.2, §9.1). Total DB round trips: 1 (distinct company ids) + 2 (batched plan
    /// resolution, SubscriptionResolver) + one per chunk of expired rows — never one per company.
    /// </summary>
    private async Task<(int Scanned, int Deleted, long BytesFreed)> DeleteExpiredPhotosAsync(CancellationToken ct)
    {
        var companyIds = await db.ClientNotePhotos.Select(p => p.CompanyId).Distinct().ToListAsync(ct);
        if (companyIds.Count == 0) return (0, 0, 0);

        var plans = await subscriptionResolver.GetEffectivePlansAsync(companyIds);
        var nowUtc = DateTime.UtcNow;

        var buckets = companyIds
            .GroupBy(id => plans[id].PhotoRetention)
            .Where(g => g.Key != Core.Enums.PhotoRetention.Forever) // US-21 p.8: never touched
            .ToList();

        var scanned = 0;
        var deleted = 0;
        long bytesFreed = 0;

        foreach (var bucket in buckets)
        {
            var ids = bucket.ToList();
            var cutoff = PhotoQuota.CutoffUtc(bucket.Key, nowUtc);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = await db.ClientNotePhotos
                    .Where(p => ids.Contains(p.CompanyId) && p.CreatedAt < cutoff)
                    .OrderBy(p => p.Id)
                    .Take(ChunkSize)
                    .ToListAsync(ct);

                if (chunk.Count == 0) break;

                scanned += chunk.Count;
                var freedThisChunk = chunk.Sum(p => p.SizeBytes);
                var paths = chunk.Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();

                // Rows go first (§1.4): a crash between this SaveChanges and the file deletes below
                // leaves orphaned files, which the sweep below picks up on a later pass — never a live
                // row pointing at a missing file.
                db.ClientNotePhotos.RemoveRange(chunk);
                await db.SaveChangesAsync(ct);

                foreach (var (full, thumb) in paths)
                {
                    storage.DeletePrivate(full);
                    storage.DeletePrivate(thumb);
                }

                deleted += chunk.Count;
                bytesFreed += freedThisChunk;

                if (chunk.Count < ChunkSize) break; // last (partial) page for this bucket
            }
        }

        return (scanned, deleted, bytesFreed);
    }

    /// <summary>
    /// Removes files under the private root that have no matching <see cref="ClientNotePhoto"/> row —
    /// the leftover of a crash between "file written" and "row committed" in the upload pipeline
    /// (ARCHITECTURE.md §4.1 steps 10-11), or of a delete that removed the row but failed to remove one
    /// of the two files. Only files older than <see cref="OrphanMinAge"/> are touched, so a photo that
    /// is mid-upload right now is never mistaken for an orphan (risk R3).
    /// </summary>
    private async Task<(int Scanned, int Deleted, long BytesFreed)> DeleteOrphanFilesAsync(CancellationToken ct)
    {
        var root = storage.PrivateRootFullPath;
        if (!Directory.Exists(root)) return (0, 0, 0);

        var scanned = 0;
        var deleted = 0;
        long bytesFreed = 0;
        var cutoffUtc = DateTime.UtcNow - OrphanMinAge;

        foreach (var companyDir in Directory.EnumerateDirectories(root))
        {
            ct.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileName(companyDir), out var companyId)) continue;

            // Chunk() (built-in, lazy) walks the directory listing batch by batch instead of
            // materializing every file name a company has ever accumulated into one List up front
            // (code review finding) — EnumerateFiles itself is already lazy, this keeps it that way.
            foreach (var batch in Directory.EnumerateFiles(companyDir).Chunk(ChunkSize))
            {
                ct.ThrowIfCancellationRequested();

                var candidates = batch
                    .Where(f => File.GetLastWriteTimeUtc(f) < cutoffUtc)
                    .Select(f => (Path: f, Key: $"{companyId}/{Path.GetFileName(f)}"))
                    .ToList();
                if (candidates.Count == 0) continue;

                scanned += candidates.Count;
                var keys = candidates.Select(c => c.Key).ToList();

                // A file is known if it's referenced as EITHER the full-size or the thumbnail path of
                // some (any) photo row — one query per batch, not per file. The SelectMany that flattens
                // (StoragePath, ThumbnailPath) into one list of keys has to happen AFTER materializing the
                // rows (code review finding): EF Core/Npgsql cannot translate a `p => new[] { a, b }`
                // projection inside the query itself, and throws InvalidOperationException at runtime for
                // every single run of this task — the exact failure QA reproduced deterministically.
                var known = await db.ClientNotePhotos
                    .Where(p => keys.Contains(p.StoragePath) || keys.Contains(p.ThumbnailPath))
                    .Select(p => new { p.StoragePath, p.ThumbnailPath })
                    .ToListAsync(ct);
                var knownSet = known.SelectMany(p => new[] { p.StoragePath, p.ThumbnailPath }).ToHashSet();

                foreach (var (path, key) in candidates)
                {
                    if (knownSet.Contains(key)) continue;
                    long size;
                    try { size = new FileInfo(path).Length; }
                    catch (IOException) { continue; } // vanished between listing and stat — fine, skip
                    try { File.Delete(path); }
                    catch (IOException ex)
                    {
                        logger.LogWarning(ex, "Could not delete orphan file {Path}", path);
                        continue;
                    }
                    deleted++;
                    bytesFreed += size;
                }
            }
        }

        return (scanned, deleted, bytesFreed);
    }

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024.0:0.0} KB"
        : $"{bytes / 1024.0 / 1024.0:0.0} MB";
}
