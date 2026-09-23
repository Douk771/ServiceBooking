using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>ARCHITECTURE_CYCLE9.md §105.11 (US-124). Deletes a <see cref="Core.Entities.PushSubscription"/>
/// whose <c>LastSuccessAtUtc</c> is older than the cutoff — OR, if it has never delivered successfully at
/// all (<c>LastSuccessAtUtc</c> is null), whose <c>CreatedAtUtc</c> is older than the cutoff. A stale
/// subscription is dead weight: the push service will reject it (or has already, via §105.8's
/// Gone-classification, which removes the row immediately on its own) — this rule is the backstop for
/// subscriptions that simply stopped being used without the push service ever saying so.</summary>
public sealed class PushSubscriptionRule(AppDbContext db) : IRetentionRule
{
    public string Name => "push-subscription";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).PushSubscription;

        IQueryable<Core.Entities.PushSubscription> Query(Guid cursor) => db.PushSubscriptions
            .Where(s => s.Id > cursor)
            .Where(s => (s.LastSuccessAtUtc != null && s.LastSuccessAtUtc < cutoff) ||
                        (s.LastSuccessAtUtc == null && s.CreatedAtUtc < cutoff))
            .OrderBy(s => s.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, s => s.Id,
            mutate: s => db.PushSubscriptions.Remove(s),
            ctx, db, ct,
            dateOf: s => s.LastSuccessAtUtc ?? s.CreatedAtUtc);
    }
}
