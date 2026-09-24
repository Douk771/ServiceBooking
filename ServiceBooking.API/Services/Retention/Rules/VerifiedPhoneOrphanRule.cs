using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>ARCHITECTURE_CYCLE12.md §151.1 — the 18th retention rule. Removes a
/// <see cref="Core.Entities.VerifiedPhone"/> row that no LIVE account currently holds (its
/// <c>UserId</c> points to a tombstone, OR its number was reassigned away — either way, no
/// <c>AppUser</c> with <c>DeletedAtUtc == null</c> currently has this exact phone), once it has aged
/// past the cutoff.
///
/// A row still attached to a live account is NEVER touched regardless of age — this is deliberate, not
/// an oversight: this rule aging out a still-current verification would silently drop the badge off a
/// person who did nothing wrong, which is exactly the "чинили бы молча" failure this doc comment exists
/// to prevent someone from introducing later.</summary>
public sealed class VerifiedPhoneOrphanRule(AppDbContext db) : IRetentionRule
{
    public string Name => "verified-phone-orphan";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).VerifiedPhoneOrphan;

        IQueryable<Core.Entities.VerifiedPhone> Query(Guid cursor) => db.VerifiedPhones
            .Where(v => v.Id > cursor)
            .Where(v => v.VerifiedAtUtc < cutoff)
            .Where(v => !db.Users.Any(u => u.PhoneNumber == v.Phone && u.DeletedAtUtc == null))
            .OrderBy(v => v.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, v => v.Id,
            mutate: v => db.VerifiedPhones.Remove(v),
            ctx, db, ct,
            dateOf: v => v.VerifiedAtUtc);
    }
}
