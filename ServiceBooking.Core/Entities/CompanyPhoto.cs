namespace ServiceBooking.Core.Entities;

// ARCHITECTURE_CYCLE10.md §102.2: a public showcase photo of the salon. Public storage class (P6) — the
// full-size and thumbnail URLs are anonymously reachable, same as Company.LogoUrl.
public class CompanyPhoto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    public string Url { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";

    // Full-size + thumbnail combined, for operational visibility — this table deliberately does NOT
    // count against PhotoQuotaMb (P5), so this is not a quota field, just an ops number.
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    // SHA-256 of the PROCESSED full-size bytes — same dedup-by-hash convention as
    // ClientNotePhoto.ContentHash, scoped per company via the unique index in AppDbContext.
    public string ContentHash { get; set; } = string.Empty;

    // 0-based; 0 = cover. Not database-unique per (CompanyId, Position) — see AppDbContext's index
    // comment and ARCHITECTURE_CYCLE10.md §102.2 for why the uniqueness is enforced by the server
    // (CompanyPhotoOrdering under an advisory lock), not by Postgres.
    public int Position { get; set; }

    public string? UploadedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public AppUser? UploadedBy { get; set; }
}

// ARCHITECTURE_CYCLE10.md §106, orphaned-files note: files under wwwroot/uploads/companies are NOT
// removed by any DB cascade — only rows are. The only two places CompanyPhoto rows can disappear today
// are (1) DELETE /api/companies/{id}/photos/{photoId}, which already deletes its own file after commit
// (CompanyPhotosController), and (2) a cascade from Company being removed. As of this cycle there is NO
// hard delete of a Company anywhere in the codebase (ProfileController.DeleteAccount's Gate #2 refuses
// to delete an account that still owns a company; deactivation only flips Company.IsActive = false) — so
// this second path is unreachable today, checked by code search, not by assumption. Whoever adds a real
// "delete company" endpoint in a future cycle MUST also delete every CompanyPhoto's Url/ThumbnailUrl file
// via FileStorage.DeletePublic before removing the row (or accept the cascade and clean up the orphaned
// files as a follow-up pass) — the row going away silently via EF's cascade is NOT enough on its own.
