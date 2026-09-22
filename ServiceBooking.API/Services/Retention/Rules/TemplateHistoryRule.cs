using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9. Deletes <see cref="Core.Entities.NotificationTemplateHistory"/> rows older than
/// <see cref="RetentionPeriods.TemplateHistoryDays"/> (§49.5's 🔴 not-below-365-days minimum is enforced
/// separately, fail-fast, by <see cref="DeploymentSafetyChecks.ValidateRetentionPeriods"/> — this rule
/// simply trusts whatever value passed that check).
/// </summary>
public sealed class TemplateHistoryRule(AppDbContext db) : IRetentionRule
{
    public string Name => "template-history";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).TemplateHistory;

        IQueryable<Core.Entities.NotificationTemplateHistory> Query(Guid cursor) => db.NotificationTemplateHistories
            .Where(h => h.Id > cursor && h.ChangedAtUtc < cutoff)
            .OrderBy(h => h.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, h => h.Id,
            mutate: h => db.NotificationTemplateHistories.Remove(h),
            ctx, db, ct,
            dateOf: h => h.ChangedAtUtc);
    }
}
