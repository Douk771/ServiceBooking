using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9. Deletes <see cref="Core.Entities.ConsentRecord"/> rows that were REVOKED more than
/// <see cref="RetentionPeriods.ConsentRecordDays"/> ago (§49.5's 🔴 not-below-1095-days minimum, fail-fast
/// enforced — see <see cref="DeploymentSafetyChecks.ValidateRetentionPeriods"/>). <c>RevokedAtUtc == null</c>
/// rows — an ACTIVE consent — are never touched by this rule at all: an active consent has no age at which
/// it "expires" on its own, only revocation starts its clock (ARCHITECTURE_CYCLE5.md §44.2: the journal is
/// the operator's evidence, ч. 1 ст. 9, for as long as the consent could still matter).
/// </summary>
public sealed class ConsentRecordRule(AppDbContext db) : IRetentionRule
{
    public string Name => "consent-record";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).ConsentRecord;

        IQueryable<Core.Entities.ConsentRecord> Query(Guid cursor) => db.ConsentRecords
            .Where(c => c.Id > cursor && c.RevokedAtUtc != null && c.RevokedAtUtc < cutoff)
            .OrderBy(c => c.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, c => c.Id,
            mutate: c => db.ConsentRecords.Remove(c),
            ctx, db, ct,
            dateOf: c => c.RevokedAtUtc!.Value);
    }
}
