using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>What a deletion removed (or, for a plan, would remove).</summary>
public sealed record ShowcaseEraseResult(ShowcaseCounts Counts, IReadOnlyList<string> FilesToDelete);

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.5 — physically removes everything marked as showcase, by the chain of <see cref="ShowcaseOwnership"/>. Physical deletion of a
/// company appears in the product for the first time here; the FK behaviours are untouched, the ORDER of statements is what makes it work.
/// Runs inside the caller's transaction (the caller holds the advisory lock and commits); files are deleted only AFTER that commit by the caller
/// (<see cref="DeleteFilesAfterCommit"/>) so a rollback never leaves rows pointing at missing files.
/// </summary>
public class ShowcaseEraser(AppDbContext db, FileStorage storage, ILogger<ShowcaseEraser> logger)
{
    /// <summary>Counts what <see cref="EraseAsync"/> would delete, without changing anything.</summary>
    public async Task<ShowcaseCounts> CountAsync(CancellationToken ct)
    {
        var counts = new Dictionary<string, int>();
        foreach (var step in ShowcaseOwnership.DeleteSteps)
            counts[step.Report] = (await db.Database.SqlQueryRaw<int>(step.CountSql).ToListAsync(ct)).Single();
        return ToCounts(counts, files: CountShowcaseFolderFiles());
    }

    /// <summary>Deletes the marked rows (inside the caller's transaction) and returns what was removed and which files are now orphaned.</summary>
    public async Task<ShowcaseEraseResult> EraseAsync(CancellationToken ct)
    {
        var files = await CollectFilesAsync(ct);
        var counts = new Dictionary<string, int>();
        foreach (var step in ShowcaseOwnership.DeleteSteps)
            counts[step.Report] = await db.Database.ExecuteSqlRawAsync(step.DeleteSql, ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("showcase erase: {Counts}", ToCounts(counts, files.Count));
        return new ShowcaseEraseResult(ToCounts(counts, files.Count), files);
    }

    /// <summary>Removes the orphaned files after the transaction committed: files that a demo visitor or staff member uploaded into a showcase company (logo,
    /// service picture, gallery photo, avatar, client-note photo) outside <c>uploads/showcase/</c>, and the content of <c>uploads/showcase/</c> itself — except
    /// <paramref name="keepShowcaseFiles"/>: after a re-seed the new rows point at the pictures just published there (same content-addressed names), and those
    /// must survive. Pass nothing (a plain delete) to remove the whole folder.</summary>
    public void DeleteFilesAfterCommit(IReadOnlyList<string> files, IReadOnlySet<string>? keepShowcaseFiles = null)
    {
        foreach (var file in files)
        {
            if (file.StartsWith("private:", StringComparison.Ordinal)) storage.DeletePrivate(file["private:".Length..]);
            else storage.DeletePublic(file);
        }
        if (keepShowcaseFiles is { Count: > 0 }) storage.DeletePublicAreaFolderExcept(PublicArea.Showcase, keepShowcaseFiles);
        else storage.DeletePublicAreaFolder(PublicArea.Showcase);
    }

    private async Task<List<string>> CollectFilesAsync(CancellationToken ct)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? url) { if (!string.IsNullOrEmpty(url) && !url.StartsWith("/uploads/showcase/", StringComparison.Ordinal)) files.Add(url); }

        foreach (var url in await db.Companies.Where(c => c.IsShowcase && c.LogoUrl != null).Select(c => c.LogoUrl).ToListAsync(ct)) Add(url);
        foreach (var url in await db.Users.Where(u => u.IsShowcase && u.AvatarUrl != null).Select(u => u.AvatarUrl).ToListAsync(ct)) Add(url);
        foreach (var url in await db.Services.Where(s => s.Company.IsShowcase && s.ImageUrl != null).Select(s => s.ImageUrl).ToListAsync(ct)) Add(url);
        foreach (var photo in await db.CompanyPhotos.Where(p => p.Company.IsShowcase).Select(p => new { p.Url, p.ThumbnailUrl }).ToListAsync(ct))
        {
            Add(photo.Url);
            Add(photo.ThumbnailUrl);
        }
        foreach (var photo in await db.ClientNotePhotos.Where(p => db.Companies.Any(c => c.Id == p.CompanyId && c.IsShowcase))
                     .Select(p => new { p.StoragePath, p.ThumbnailPath }).ToListAsync(ct))
        {
            if (!string.IsNullOrEmpty(photo.StoragePath)) files.Add("private:" + photo.StoragePath);
            if (!string.IsNullOrEmpty(photo.ThumbnailPath)) files.Add("private:" + photo.ThumbnailPath);
        }
        return files.ToList();
    }

    private int CountShowcaseFolderFiles() => storage.CountPublicAreaFiles(PublicArea.Showcase);

    private static ShowcaseCounts ToCounts(Dictionary<string, int> c, int files) => new(
        c.GetValueOrDefault("companies"), c.GetValueOrDefault("users"), c.GetValueOrDefault("billingAccounts"), c.GetValueOrDefault("services"),
        c.GetValueOrDefault("bookings"), c.GetValueOrDefault("bookingEvents"), c.GetValueOrDefault("photos"), files);
}
