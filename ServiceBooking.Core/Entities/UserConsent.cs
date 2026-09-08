using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// The last accepted version of one legal document by one user (US-37, ARCHITECTURE.md §5.1). No
/// journal is kept — the row is overwritten on re-acceptance (SPEC §3.3 p.2 allows this); the unique
/// index on (UserId, DocumentType) makes "at most one row per document per user" a hard DB guarantee.
/// </summary>
public class UserConsent
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public LegalDocumentType DocumentType { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;

    public AppUser User { get; set; } = null!;
}
