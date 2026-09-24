using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>ARCHITECTURE_CYCLE12.md §151.1 (R11, R13) — the 17th retention rule. Deletes a
/// <see cref="Core.Entities.PhoneVerificationSession"/> row of ANY status older than the cutoff: the
/// session itself lives minutes (its own TTL) to at most
/// <c>PhoneVerification:VerifiedSessionUsableMinutes</c> past completion, so a one-day retention window
/// is purely incident-review slack, never a working lifetime this rule is racing against.
///
/// ⚠️ Registered LAST among cycle 12's two new rules in <c>Program.cs</c> — <c>DataRetentionTask</c>'s
/// own unprotected <c>foreach</c> (a pre-existing gap, <c>N9-6</c>) means an exception in an EARLIER rule
/// stops this one from running that pass. Not fixed here (out of scope, §3 of the spec) — recorded so it
/// does not get silently "fixed" by reordering registrations without also fixing N9-6 itself.</summary>
public sealed class PhoneVerificationSessionRule(AppDbContext db) : IRetentionRule
{
    public string Name => "phone-verification-session";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).PhoneVerificationSession;

        IQueryable<Core.Entities.PhoneVerificationSession> Query(Guid cursor) => db.PhoneVerificationSessions
            .Where(s => s.Id > cursor)
            .Where(s => s.CreatedAtUtc < cutoff)
            .OrderBy(s => s.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, s => s.Id,
            mutate: s => db.PhoneVerificationSessions.Remove(s),
            ctx, db, ct,
            dateOf: s => s.CreatedAtUtc);
    }
}
