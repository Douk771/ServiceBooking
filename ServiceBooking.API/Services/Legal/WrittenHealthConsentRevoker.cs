using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Legal;

public sealed record WrittenHealthConsentRevokeResult(int Revoked, int HealthNotesDeleted);

/// <summary>
/// ARCHITECTURE_CYCLE20.md §402.4 (US-20-01) — the ONE cascade shared by all four entry points that lift
/// a <see cref="LegalTextKey.HealthDataWrittenConsentForm"/> mark: staff revoking it directly, a client
/// revoking it (or the salon-scoped <c>HealthDataConsent</c>) from their profile for one company, and a
/// client revoking their whole <c>PdnConsent</c>/<c>HealthData</c> purpose across every company. Each call
/// stamps a revoke on every currently-live mark that matches AND deletes the matching
/// <see cref="Core.Entities.ClientHealthNote"/> rows, in ONE transaction — never one without the other.
///
/// Deliberately does NOT go through <see cref="ConsentLedger"/>'s per-subject API: a phone-scoped
/// <see cref="ConsentSubject"/> always names exactly one company, and the "every company" entry point
/// (a null <c>companyId</c>) has no single subject to name. This class touches
/// <see cref="Core.Entities.ConsentRecord"/> directly instead, for that one reason only.
///
/// Every caller MUST have already resolved the subject's phone through the TD-03 gate
/// (<c>SubjectScopeResolver</c>) or, for the staff entry point, through the company's own client
/// resolution — <see cref="RevokeAsync"/> trusts the phone it is given and does not re-derive it.
///
/// Composable under an AMBIENT transaction: <c>ProfileConsentsController.ApplyOrPreviewRevokeEffectsAsync</c> (moved out of ProfileController by cycle 22)
/// already has one open (shared with its OTHER cascades — photos, queued notifications, profile fields)
/// by the time it needs to call this for the whole-PdnConsent/HealthData-purpose entry point.
/// <see cref="RevokeAsync"/> only opens (and later commits) its OWN transaction and advisory lock when
/// <c>db.Database.CurrentTransaction</c> is null — starting a SECOND transaction on the same connection
/// while one is already active throws.
/// </summary>
public sealed class WrittenHealthConsentRevoker(AppDbContext db)
{
    /// <param name="phone">Canonical phone of the subject whose marks are being lifted — already
    /// resolved/gated by the caller.</param>
    /// <param name="companyId">Null revokes/deletes across EVERY company for this phone (the
    /// whole-PdnConsent/HealthData-purpose entry point); a value scopes to just that one company.</param>
    /// <param name="reason">One of <see cref="WrittenHealthConsentTexts"/>' fixed strings.</param>
    /// <param name="revokedByUserId">Who lifted the mark — a staff member's id, or null when the subject
    /// did it themselves from their profile.</param>
    /// <param name="healthNoteClientId">The account's userId, if any, so a health note filed under the
    /// account (ClientId) is deleted too, not just guest-phone-keyed ones — null for a caller with no
    /// account (a pure guest revoking is not reachable today, but the parameter stays honest either way).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<WrittenHealthConsentRevokeResult> RevokeAsync(
        string phone, Guid? companyId, string? reason, string? revokedByUserId, string? healthNoteClientId, CancellationToken ct = default)
    {
        var ownsTransaction = db.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        if (ownsTransaction)
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            await AdvisoryLock.AcquireAsync(db, $"consent:health-written:{phone}");
        }

        try
        {
            var recordsQuery = db.ConsentRecords.Where(c =>
                c.SubjectPhone == phone && c.DocumentKey == LegalTextKey.HealthDataWrittenConsentForm && c.RevokedAtUtc == null);  // SUBJECT-PHONE-GATE: gated — every caller resolves phone via SubjectScopeResolver (account entry points) or the company's own client resolution (staff entry point) before calling this method, ARCHITECTURE_CYCLE16.md §245.4
            if (companyId is not null)
                recordsQuery = recordsQuery.Where(c => c.CompanyId == companyId);

            var liveRecords = await recordsQuery.ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var record in liveRecords)
            {
                record.RevokedAtUtc = now;
                record.RevokeReason = reason;
                record.RevokedByUserId = revokedByUserId;
            }

            var notesQuery = db.ClientHealthNotes.Where(n =>
                n.GuestPhone == phone || (healthNoteClientId != null && n.ClientId == healthNoteClientId));  // SUBJECT-PHONE-GATE: gated — same already-resolved phone as above
            if (companyId is not null)
                notesQuery = notesQuery.Where(n => n.CompanyId == companyId);

            var notes = await notesQuery.ToListAsync(ct);
            if (notes.Count > 0)
                db.ClientHealthNotes.RemoveRange(notes);

            await db.SaveChangesAsync(ct);
            if (ownsTransaction) await transaction!.CommitAsync(ct);

            return new WrittenHealthConsentRevokeResult(liveRecords.Count, notes.Count);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }
}
