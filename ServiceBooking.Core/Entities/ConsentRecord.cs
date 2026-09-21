using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// One event in the consent journal — a row is never updated except to stamp RevokedAtUtc/RevokeReason
/// on it (ARCHITECTURE_CYCLE5.md §41 principle 1, §44.2). Replaces UserConsent (cycle 3), which stored
/// "current state" as a single overwritable row per (UserId, DocumentType); this table has no uniqueness
/// constraint at all — "what is currently true" is a query (ConsentLedger), not a row.
///
/// One table serves both subjects with an account (UserId set, SubjectPhone/CompanyId null) and subjects
/// without one — a guest who gave a salon-facing consent (SubjectPhone set, CompanyId set for the salon
/// that collected it, UserId null). Two tables would mean two readers, two exports and two places to
/// remember on every future change; the cost of one table is the two different partial indexes below
/// instead of a single unique one (§44.2 p.1).
///
/// DocumentKey is a plain string, not LegalDocumentType — deliberately: it must also carry the six
/// interface-text keys (LegalTextKey) that PdnConsent/salon consents are recorded against, and none of
/// those has (or should have) a LegalDocumentType member (§43.1, §44.1). DocumentHash is SHA-256 of the
/// exact byte content shown, not a copy of the text — 64 bytes vs kilobytes, and a deliberate tripwire:
/// if an operator swaps a file without bumping its version, old rows keep a hash nothing on disk matches
/// any more, and that mismatch is what proves the swap happened (§44.2 p.2).
/// </summary>
public class ConsentRecord
{
    public Guid Id { get; set; }

    public string? UserId { get; set; }
    public string? SubjectPhone { get; set; }
    public Guid? CompanyId { get; set; }

    public string DocumentKey { get; set; } = string.Empty;
    public string DocumentVersion { get; set; } = string.Empty;
    public string DocumentHash { get; set; } = string.Empty;

    public ConsentPurpose? Purpose { get; set; }
    public ConsentAct Act { get; set; }
    public ConsentSource Source { get; set; }

    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

    // Evidentiary metadata only — never rendered back through the API (API_CONTRACT_CYCLE5.md §38.4:
    // "IP и User-Agent наружу не отдаются никогда"). Nullable: Migrated rows (dev/test only, §44.2 p.7)
    // never had this captured in the first place.
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    // Set when a staff member confirms a purpose-limited consent FOR the subject in a salon context
    // (photo/health forms, §48.3) — the subject themselves is UserId/SubjectPhone above; this is who
    // operated the form.
    public string? RecordedByUserId { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokeReason { get; set; }

    public AppUser? User { get; set; }
    public Company? Company { get; set; }
    public AppUser? RecordedByUser { get; set; }
}
