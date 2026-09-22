using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// One grant to write (ARCHITECTURE_CYCLE5.md §45.1). DocumentHash is the SHA-256 (hex) of the exact
/// document/text content shown to the subject at the moment of the act — callers read it off the
/// LegalSnapshot/LegalDocument they already loaded to validate the version, never recomputed here.
/// </summary>
public sealed record ConsentGrant(
    ConsentSubject Subject,
    string DocumentKey,
    string DocumentVersion,
    string DocumentHash,
    ConsentPurpose? Purpose,
    ConsentAct Act,
    ConsentSource Source,
    string? IpAddress = null,
    string? UserAgent = null,
    string? RecordedByUserId = null);

/// <summary>Read-only projection of a ConsentRecord row for CurrentAsync/CurrentAllAsync/HistoryAsync —
/// callers of the ledger get a value they can't accidentally track/save through, only GrantAsync/
/// RevokeAsync write.</summary>
public sealed record ConsentState(
    Guid Id,
    string DocumentKey,
    ConsentPurpose? Purpose,
    string DocumentVersion,
    ConsentAct Act,
    ConsentSource Source,
    Guid? CompanyId,
    DateTime GrantedAtUtc,
    DateTime? RevokedAtUtc,
    string? RevokeReason);

/// <summary>
/// The single reader and single writer of ConsentRecords (ARCHITECTURE_CYCLE5.md §45.1, §45.4). Scoped,
/// not singleton: it holds no state of its own between calls, it just wraps AppDbContext queries that
/// happen to all live in one place so that "what does 'currently consented' mean" is answered exactly
/// once in the codebase.
///
/// Deliberately NOT used on the hot path (every authenticated request) — §45.1's whole point is that
/// Privacy/TermsClient/TermsOwner are checked via JWT claims against the in-memory LegalSnapshot
/// (LegalConsentFilter, TokenService), zero database queries. This class is only reached from the four
/// "cold" call sites named in §45.1: the consent-management screens, notification queueing, photo
/// upload, and health-note read/write — each of which already talks to the database for its own reasons.
/// </summary>
public class ConsentLedger(AppDbContext db)
{
    /// <summary>The current (latest, non-revoked) grant for one document key and purpose, or null if none
    /// exists or the latest one was revoked. Purpose must match exactly — including null-to-null, for
    /// document keys (Privacy, TermsClient, TermsOwner's CompanyCreation row) that are never purpose-scoped.</summary>
    public async Task<ConsentState?> CurrentAsync(ConsentSubject subject, string documentKey, ConsentPurpose? purpose, CancellationToken ct = default)
    {
        var record = await CurrentRecordsQuery(subject)
            .Where(c => c.DocumentKey == documentKey && c.Purpose == purpose)
            .OrderByDescending(c => c.GrantedAtUtc)
            .FirstOrDefaultAsync(ct);

        return record is null ? null : ToState(record);
    }

    /// <summary>Every (DocumentKey, Purpose) combination this subject currently has a live grant for —
    /// one row per combination, the most recent non-revoked one. Used to render "Мои согласия"
    /// (API_CONTRACT_CYCLE5.md §41.1 `granted`), never the full journal (that's a separate, explicit
    /// full-history read — this method's whole purpose is to answer "what applies right now").</summary>
    public async Task<IReadOnlyList<ConsentState>> CurrentAllAsync(ConsentSubject subject, CancellationToken ct = default)
    {
        var candidates = await CurrentRecordsQuery(subject)
            .OrderByDescending(c => c.GrantedAtUtc)
            .ToListAsync(ct);

        // Grouped in memory, not in SQL: per-subject row counts are small (a handful of document keys
        // times a handful of purposes), and a window-function/DISTINCT ON translation would buy nothing
        // here but complexity — this method is never on the hot path (see class comment).
        return candidates
            .GroupBy(c => (c.DocumentKey, c.Purpose))
            .Select(g => ToState(g.First())) // already ordered by GrantedAtUtc desc
            .ToList();
    }

    /// <summary>The FULL journal for one subject — every row, revoked or not, newest first. Used where a
    /// human needs to see everything they ever did (API_CONTRACT_CYCLE5.md §41.1 `history`, §49
    /// `consents` export section), never for an access-control decision (that's CurrentAsync/
    /// CurrentAllAsync above — a revoked row must never look "current" to anything but this audit view).
    ///
    /// Code review В3: for a <see cref="ConsentSubject.ForUser"/> subject, <paramref name="knownPhone"/>
    /// (the account's current canonical phone, when known) additionally pulls in every row recorded
    /// about this SAME real person while a company's staff dealt with them by phone — salon-recorded
    /// `PhotoConsent`/`HealthDataConsent` rows (`UserId == null`, `SubjectPhone` + `CompanyId` set,
    /// ClientConsentsController). Without this, "Мои согласия" (§41.1: "человек видит всё, что
    /// подписывал") silently omitted every consent a salon ever recorded on their behalf — the two rows
    /// share no `UserId`, only the same phone number, so a plain `UserId` filter can never find them.
    /// Optional and defaults to null (no salon rows pulled) so every OTHER caller of this method —
    /// ForPhoneInCompany reads, and any ForUser read that has no phone to offer — keeps its exact prior
    /// behavior.</summary>
    public async Task<IReadOnlyList<ConsentState>> HistoryAsync(ConsentSubject subject, string? knownPhone = null, CancellationToken ct = default)
    {
        List<ConsentRecord> records;
        if (subject.UserId is not null)
        {
            var byUser = await db.ConsentRecords.Where(c => c.UserId == subject.UserId).ToListAsync(ct);
            if (string.IsNullOrEmpty(knownPhone))
            {
                records = byUser;
            }
            else
            {
                var byPhone = await db.ConsentRecords
                    .Where(c => c.UserId == null && c.SubjectPhone == knownPhone).ToListAsync(ct);
                records = byUser.Concat(byPhone).ToList();
            }
        }
        else
        {
            records = await db.ConsentRecords
                .Where(c => c.SubjectPhone == subject.Phone && c.CompanyId == subject.CompanyId).ToListAsync(ct);
        }

        return records.OrderByDescending(c => c.GrantedAtUtc).Select(ToState).ToList();
    }

    /// <summary>
    /// Records one grant. Idempotent within a short window (ARCHITECTURE_CYCLE5.md §45.4): a duplicate
    /// call for the same subject/key/purpose/version inside 5 seconds returns the existing row instead of
    /// inserting a second one — the guard against a double-click/retry now that the unique index that
    /// used to do this job (cycle 3's UserConsent) is gone on purpose (§44.2 p.4, that IS US-66 p.1). A
    /// genuinely new acceptance of the same version a month later is a legitimate second event and gets
    /// its own row — the window is seconds, not "ever".
    /// </summary>
    public async Task<ConsentRecord> GrantAsync(ConsentGrant grant, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, grant.Subject.LockKey);

        var now = DateTime.UtcNow;
        var recent = await CurrentRecordsQuery(grant.Subject)
            .Where(c => c.DocumentKey == grant.DocumentKey
                        && c.Purpose == grant.Purpose
                        && c.DocumentVersion == grant.DocumentVersion
                        && c.GrantedAtUtc > now.AddSeconds(-5))
            .OrderByDescending(c => c.GrantedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (recent is not null)
        {
            await transaction.CommitAsync(ct); // nothing was written, but the lock must still be released cleanly
            return recent;
        }

        var record = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            UserId = grant.Subject.UserId,
            SubjectPhone = grant.Subject.Phone,
            CompanyId = grant.Subject.CompanyId,
            DocumentKey = grant.DocumentKey,
            DocumentVersion = grant.DocumentVersion,
            DocumentHash = grant.DocumentHash,
            Purpose = grant.Purpose,
            Act = grant.Act,
            Source = grant.Source,
            GrantedAtUtc = now,
            IpAddress = grant.IpAddress,
            UserAgent = grant.UserAgent,
            RecordedByUserId = grant.RecordedByUserId
        };

        db.ConsentRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return record;
    }

    /// <summary>
    /// Stamps RevokedAtUtc/RevokeReason on every currently-live row that matches (never deletes a row,
    /// §41.3/§47.3/§44.2 principle 1). `purpose: null` revokes the whole document key — every purpose at
    /// once, matching API_CONTRACT_CYCLE5.md §41.3's "purpose: null → отзывается согласие целиком".
    /// Returns the number of rows actually revoked; 0 is a legitimate, idempotent answer ("nothing to
    /// revoke"), not an error (US-68 p.7).
    /// </summary>
    public async Task<int> RevokeAsync(ConsentSubject subject, string documentKey, ConsentPurpose? purpose, string? reason, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, subject.LockKey);

        var query = CurrentRecordsQuery(subject).Where(c => c.DocumentKey == documentKey);
        if (purpose is not null)
            query = query.Where(c => c.Purpose == purpose);

        var toRevoke = await query.ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var record in toRevoke)
        {
            record.RevokedAtUtc = now;
            record.RevokeReason = reason;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return toRevoke.Count;
    }

    /// <summary>The one place both partial indexes (IX_ConsentRecords_CurrentByUser,
    /// IX_ConsentRecords_CurrentBySubject) are matched — every other query method filters further from
    /// here, so a query that doesn't hit one of the two indexes can't be written by accident.</summary>
    private IQueryable<ConsentRecord> CurrentRecordsQuery(ConsentSubject subject)
    {
        var query = db.ConsentRecords.Where(c => c.RevokedAtUtc == null);
        return subject.UserId is not null
            ? query.Where(c => c.UserId == subject.UserId)
            : query.Where(c => c.SubjectPhone == subject.Phone && c.CompanyId == subject.CompanyId);
    }

    private static ConsentState ToState(ConsentRecord r) =>
        new(r.Id, r.DocumentKey, r.Purpose, r.DocumentVersion, r.Act, r.Source, r.CompanyId, r.GrantedAtUtc, r.RevokedAtUtc, r.RevokeReason);
}
