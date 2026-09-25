using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// Creation, lookup and cancellation of a <see cref="PhoneVerificationSession"/> — the DB-backed half of
/// §145.1's lifecycle (ARCHITECTURE_CYCLE14.md §144.3); the actual state transitions when an update
/// arrives from the bot live in <c>Max.PhoneVerificationWebhookHandler</c> instead, since only THAT code
/// path knows how to evaluate a check like "does the signature match".
/// </summary>
public sealed class PhoneVerificationSessionService(
    AppDbContext db, IPhoneVerificationMethodRegistry registry, IOptions<PhoneVerificationOptions> options)
{
    public enum StartOutcome
    {
        Started,

        /// <summary>The subsystem is switched off (or the one method it has is not currently
        /// Enabled) — §163's 409.</summary>
        SubsystemDisabled,

        /// <summary>§150.4/Q8: <c>MaxOpenSessionsPerPhone</c> already-open sessions exist for this
        /// canonical phone — cheap anti-abuse independent of the rate-limiting policy.</summary>
        TooManyOpenSessions,
    }

    public sealed record StartResult(
        StartOutcome Outcome, PhoneVerificationSession? Session, string? StatusToken, string? DeepLink, string? WebLink, byte[]? QrPng);

    public async Task<StartResult> StartAsync(string canonicalPhone, string? userId, PhoneVerificationPurpose purpose, CancellationToken ct)
    {
        // Cycle 14 has exactly one method — §144.2 explicitly forbids a "pick a method" step existing at
        // all. A future second method picks among registry.Registered instead of this literal.
        var adapter = registry.Get(PhoneVerificationMethod.MaxBot);
        if (!adapter.Enabled)
            return new StartResult(StartOutcome.SubsystemDisabled, null, null, null, null, null);

        var now = DateTime.UtcNow;
        var openSessionsCount = await db.PhoneVerificationSessions.CountAsync(s =>
            s.CanonicalPhone == canonicalPhone &&  // SUBJECT-PHONE-GATE: not-account-scoped — cycle 14 phone-verification subsystem itself (ARCHITECTURE_CYCLE16.md §245.3)
            (s.Status == PhoneVerificationStatus.Pending || s.Status == PhoneVerificationStatus.Linked) &&
            s.ExpiresAtUtc > now, ct);
        if (openSessionsCount >= options.Value.MaxOpenSessionsPerPhone)
            return new StartResult(StartOutcome.TooManyOpenSessions, null, null, null, null, null);

        var session = new PhoneVerificationSession
        {
            Id = Guid.NewGuid(),
            Purpose = purpose,
            Method = adapter.Method,
            CanonicalPhone = canonicalPhone,
            UserId = userId,
            Status = PhoneVerificationStatus.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(Math.Max(1, options.Value.SessionTtlMinutes)),
        };

        // §145.1 step 1: no outbound call happens here — StartAsync only generates the payload/deep
        // link/QR, it never reaches out to MAX.
        var challenge = await adapter.StartAsync(session, ct);
        session.PayloadHash = challenge.PayloadHash;

        var statusToken = StatusTokenGenerator.Generate();
        session.StatusTokenHash = StatusTokenGenerator.Hash(statusToken);

        db.PhoneVerificationSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new StartResult(StartOutcome.Started, session, statusToken, challenge.DeepLink, challenge.WebLink, challenge.QrPng);
    }

    /// <summary>§164/§166's shared lookup: the pair {sessionId, statusToken} is the ONLY thing that
    /// proves the caller may see/cancel this session — a wrong or missing token returns null exactly like
    /// an unknown id, so the endpoint can never be used to enumerate sessions (§164's own note).</summary>
    public async Task<PhoneVerificationSession?> FindByTokenAsync(Guid sessionId, string statusToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(statusToken)) return null;

        var session = await db.PhoneVerificationSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;

        var tokenHash = StatusTokenGenerator.Hash(statusToken);
        return CryptographicEquals(session.StatusTokenHash, tokenHash) ? session : null;
    }

    /// <summary>§166: ALWAYS succeeds (204), even when nothing was there to cancel — the endpoint must
    /// not reveal "no such session" through its status code.</summary>
    public async Task CancelAsync(Guid sessionId, string statusToken, CancellationToken ct)
    {
        var session = await FindByTokenAsync(sessionId, statusToken, ct);
        if (session is null) return;

        if (session.Status is PhoneVerificationStatus.Pending or PhoneVerificationStatus.Linked)
        {
            session.Status = PhoneVerificationStatus.Cancelled;
            session.FailureReason = PhoneVerificationFailureReason.SessionCancelled;
            session.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    // Hashes are already SHA-256 hex of a high-entropy random token — not secret material an attacker
    // can brute-force from a timing side channel the way a password could be, but compared this way
    // anyway (cheap, and it costs nothing to be consistent with §147.1's own constant-time comparison).
    private static bool CryptographicEquals(string a, string b) =>
        a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
