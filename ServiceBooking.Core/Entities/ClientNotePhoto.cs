namespace ServiceBooking.Core.Entities;

public class ClientNotePhoto
{
    public Guid Id { get; set; }
    public Guid ClientNoteId { get; set; }

    // Denormalised copy of ClientNote.CompanyId. Two readers need it without a join: the quota sum (one
    // indexed aggregate per company, ARCHITECTURE.md §6.1) and the private download endpoint (one row ->
    // one permission check). Written once from the note at insert time and never updated — a note never
    // changes company.
    public Guid CompanyId { get; set; }

    // "<companyId>/<guid>.jpg" — an opaque FileStorage key, NOT a URL. See FileStorage's class doc for
    // why the private class never produces something a browser could use directly.
    public string StoragePath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";

    // Full-size + thumbnail combined: the quota promises the owner disk space, and both files occupy it
    // (ARCHITECTURE.md §6.1). One row, one SizeBytes, the whole footprint.
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    // SHA-256 of the PROCESSED bytes (post resize/re-encode), not the bytes the client uploaded — a
    // double form submission or network retry produces byte-identical processed output even if the
    // original file's bytes differed trivially (e.g. different embedded metadata later stripped), and
    // two visually different photos that happen to resize to the same output are a real duplicate by
    // the only definition that matters here. Enforced by a unique index on (ClientNoteId, ContentHash)
    // — see AppDbContext — so quota is never charged twice for one logical upload (§6.3).
    public string ContentHash { get; set; } = string.Empty;

    // Nullable + SetNull (see AppDbContext): deleting the uploader's account must not delete company
    // data — notes and photos belong to the company, not to the employee who happened to upload them
    // (same rule as ClientNote.MasterId's comment and RemoveMember, US-20 p.4). This is also what makes
    // the phone-normalisation migration's account merges safe (ARCHITECTURE.md §14.2).
    public string? UploadedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ClientNote ClientNote { get; set; } = null!;
    public AppUser? UploadedBy { get; set; }
}
