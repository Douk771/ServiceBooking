using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// The ONLY writer of <see cref="VerifiedPhone"/> and of <c>AppUser.PhoneNumberConfirmed</c>
/// (ARCHITECTURE_CYCLE14.md §142.4) — no controller touches either directly, the same convention this
/// codebase already applies to <c>BookingEventLog</c>/<c>IdentityRoleSync</c>. Callers are responsible
/// for the surrounding transaction and for <c>AdvisoryLock.AcquireAsync(db, $"phone-verification:{key}")</c>
/// (§145.2) — this class only decides WHAT to write, never when to lock or commit.
/// </summary>
public sealed class PhoneVerificationWriter(AppDbContext db)
{
    public enum WriteOutcome
    {
        Written,

        /// <summary>§147.5 — the MAX account already has the configured ceiling of OTHER numbers
        /// verified. The caller is expected to have already checked this same condition before deciding
        /// to call <see cref="WriteAsync"/> at all (defence in depth: the check is repeated here, inside
        /// the lock, so a race between the pre-check and this call can never slip through).</summary>
        LimitReached,
    }

    /// <param name="session">A session whose <c>ExternalAccountKey</c> is set and whose
    /// <c>CanonicalPhone</c> is the number being verified.</param>
    /// <param name="user">The account the number should be attributed to — null for a
    /// <c>Registration</c>-purpose session being confirmed before the account exists yet (the row is
    /// created with no <c>UserId</c>; <c>AuthController.Register</c> is a separate write path, see its
    /// own comment).</param>
    /// <param name="maxPhonesPerExternalAccount">Р5's configured ceiling.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<WriteOutcome> WriteAsync(PhoneVerificationSession session, AppUser? user, int maxPhonesPerExternalAccount, CancellationToken ct)
    {
        var externalAccountKey = session.ExternalAccountKey
            ?? throw new InvalidOperationException("WriteAsync requires a session that has already been linked (ExternalAccountKey set).");

        // §147.5: counts every OTHER number this MAX account has verified — re-verifying the SAME number
        // never costs a slot.
        var otherPhonesCount = await db.VerifiedPhones.CountAsync(
            v => v.ExternalAccountKey == externalAccountKey && v.Phone != session.CanonicalPhone, ct);
        if (otherPhonesCount >= maxPhonesPerExternalAccount)
            return WriteOutcome.LimitReached;

        var now = DateTime.UtcNow;
        var existing = await db.VerifiedPhones.FirstOrDefaultAsync(v => v.Phone == session.CanonicalPhone, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — this IS the writer that populates VerifiedPhones, TD-03's source of truth, not a reader of it (ARCHITECTURE_CYCLE16.md §245.3)
        if (existing is null)
        {
            db.VerifiedPhones.Add(new VerifiedPhone
            {
                Id = Guid.NewGuid(),
                Phone = session.CanonicalPhone,
                Method = session.Method,
                ExternalAccountKey = externalAccountKey,
                VerifiedAtUtc = now,
                UserId = user?.Id,
                SessionId = session.Id,
            });
        }
        else
        {
            // §142.2: re-verifying the same number (same or different MAX account) updates the row in
            // place rather than creating a duplicate — "перенос номера на другой MAX-аккаунт разрешён".
            existing.Method = session.Method;
            existing.ExternalAccountKey = externalAccountKey;
            existing.VerifiedAtUtc = now;
            existing.UserId = user?.Id;
            existing.SessionId = session.Id;
        }

        // §142.4: the mirror is written in the SAME transaction the caller owns.
        if (user is not null)
            user.PhoneNumberConfirmed = true;

        return WriteOutcome.Written;
    }

    /// <summary>§148.5 step 6 (US-14-11): a successful number change removes the OLD number's
    /// verification outright — the badge must not keep pointing at a number this account no longer
    /// holds. Does not touch the mirror; the caller sets <c>PhoneNumberConfirmed</c> itself based on
    /// whether the NEW number is verified by the session it just consumed.</summary>
    public async Task RemoveForOldNumberAsync(string oldCanonicalPhone, string userId, CancellationToken ct)
    {
        var rows = await db.VerifiedPhones
            .Where(v => v.Phone == oldCanonicalPhone && v.UserId == userId)  // SUBJECT-PHONE-GATE: not-account-scoped — this IS the writer maintaining VerifiedPhones itself, scoped to its own UserId (ARCHITECTURE_CYCLE16.md §245.3)
            .ToListAsync(ct);
        db.VerifiedPhones.RemoveRange(rows);
    }
}
