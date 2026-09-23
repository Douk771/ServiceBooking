using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services;

/// <summary>Thrown when PUT /api/companies/{id}/photos/order is asked to apply something other than a
/// full permutation of the company's current photos — API_CONTRACT_CYCLE10.md §128.2.</summary>
public sealed class InvalidPhotoReorderException()
    : Exception("Список должен содержать все фотографии салона ровно по одному разу");

/// <summary>
/// Pure ordering/compaction logic for <see cref="CompanyPhoto"/> (ARCHITECTURE_CYCLE10.md §102.2, §106).
/// No DB, no HTTP — callers are responsible for loading the company's photos, calling one of these
/// methods to mutate <see cref="CompanyPhoto.Position"/> in place, and running <c>SaveChangesAsync</c>
/// inside a transaction held under the advisory lock <c>company-photos:{companyId}</c>. Deliberately not
/// relying on a unique (CompanyId, Position) database index — see the AppDbContext config comment for
/// why a non-deferrable Postgres unique index can't survive EF's row-by-row UPDATEs during a reorder.
/// </summary>
public static class CompanyPhotoOrdering
{
    /// <summary>§125/§127: at most 10 showcase photos per company.</summary>
    public const int MaxPhotosPerCompany = 10;

    /// <summary>
    /// Applies a full permutation: <paramref name="photoIds"/> must contain every id in
    /// <paramref name="currentPhotos"/> exactly once, in the new desired order (first = new cover). Sets
    /// each photo's <see cref="CompanyPhoto.Position"/> to its index in that list. Throws
    /// <see cref="InvalidPhotoReorderException"/> for anything short of a full, no-repeat permutation —
    /// a partial list, an unknown id, or a duplicate id.
    /// </summary>
    public static void ApplyOrder(IReadOnlyList<CompanyPhoto> currentPhotos, IReadOnlyList<Guid> photoIds)
    {
        if (photoIds.Count != currentPhotos.Count || photoIds.Distinct().Count() != photoIds.Count)
            throw new InvalidPhotoReorderException();

        var byId = currentPhotos.ToDictionary(p => p.Id);
        if (photoIds.Any(id => !byId.ContainsKey(id)))
            throw new InvalidPhotoReorderException();

        for (var i = 0; i < photoIds.Count; i++)
            byId[photoIds[i]].Position = i;
    }

    /// <summary>
    /// Renumbers whatever photos remain (after a delete, or as a defensive normalization) into a
    /// contiguous 0..n-1 range, preserving the deterministic read order §102.2 mandates everywhere:
    /// by <see cref="CompanyPhoto.Position"/>, then <see cref="CompanyPhoto.CreatedAtUtc"/>, then
    /// <see cref="CompanyPhoto.Id"/>. If the deleted photo was the cover (Position 0), the next one in
    /// that order becomes the new cover — exactly by virtue of getting Position 0 here.
    /// </summary>
    public static void Compact(IReadOnlyList<CompanyPhoto> remainingPhotos)
    {
        var ordered = remainingPhotos
            .OrderBy(p => p.Position)
            .ThenBy(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Position = i;
    }

    /// <summary>
    /// Picks one cover photo per company out of a raw batch of Position == 0 rows (ARCHITECTURE_CYCLE10.md
    /// §102.2). The (CompanyId, Position) index is deliberately NON-unique, so more than one row per
    /// company can legitimately arrive here — plain <c>ToDictionary</c> would throw ArgumentException on
    /// the duplicate key and take down every caller (the anonymous public catalog included). Instead,
    /// group by company and take the row that <see cref="Compact"/>'s read order would also pick first:
    /// lowest Position, then oldest CreatedAtUtc, then lowest Id.
    /// </summary>
    public static Dictionary<Guid, CompanyPhoto> SelectCovers(IEnumerable<CompanyPhoto> positionZeroPhotos) =>
        positionZeroPhotos
            .GroupBy(p => p.CompanyId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id).First());
}
