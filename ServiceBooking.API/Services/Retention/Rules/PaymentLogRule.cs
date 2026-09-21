using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>T5-B8/B9. Deletes a <see cref="Core.Entities.ChannelPaymentLog"/> older than
/// <see cref="RetentionPeriods.PaymentLogDays"/> (default 5 years — primary accounting document,
/// ст. 29 ФЗ «О бухгалтерском учёте», §13.5: "срок требует проверки" — kept as configuration for exactly
/// that reason, so an accountant's correction is a config edit, not a redeploy).</summary>
public sealed class PaymentLogRule(AppDbContext db) : IRetentionRule
{
    public string Name => "payment-log";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).PaymentLog;

        IQueryable<Core.Entities.ChannelPaymentLog> Query(Guid cursor) => db.ChannelPaymentLogs
            .Where(p => p.Id > cursor && p.ChangedAtUtc < cutoff)
            .OrderBy(p => p.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, p => p.Id,
            mutate: p => db.ChannelPaymentLogs.Remove(p),
            ctx, db, ct,
            dateOf: p => p.ChangedAtUtc);
    }
}
