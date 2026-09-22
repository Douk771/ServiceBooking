using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>T5-B8/B9. Deletes a <see cref="Core.Entities.MailLog"/> row older than
/// <see cref="RetentionPeriods.MailLogDays"/> (§13.5: "функция — заглушка… хранить ПДн ради нерабочей
/// функции — обработка без цели"; the lawyer's alternative — remove the entity outright — is a product
/// decision this task does not make on its own, so the row is aged out instead).</summary>
public sealed class MailLogRule(AppDbContext db) : IRetentionRule
{
    public string Name => "mail-log";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).MailLog;

        IQueryable<Core.Entities.MailLog> Query(Guid cursor) => db.MailLogs
            .Where(m => m.Id > cursor && m.SentAt < cutoff)
            .OrderBy(m => m.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, m => m.Id,
            mutate: m => db.MailLogs.Remove(m),
            ctx, db, ct,
            dateOf: m => m.SentAt);
    }
}
