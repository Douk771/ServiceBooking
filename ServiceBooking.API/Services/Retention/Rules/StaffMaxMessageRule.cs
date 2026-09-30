using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §508 [legal L20]. Deletes a <see cref="Core.Entities.StaffMaxMessage"/> queue/journal row older than
/// <see cref="RetentionPeriods.StaffMaxMessageDays"/> (default 90) outright. The text carries no customer personal data (number, shop, time, amount), and
/// the addressee is only an opaque chat key.
/// </summary>
public sealed class StaffMaxMessageRule(AppDbContext db) : IRetentionRule
{
    public string Name => "staff-max-messages";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = ctx.NowUtc.AddDays(-Math.Max(1, ctx.Periods.StaffMaxMessageDays));

        return RetentionRuleRunner.RunAsync(
            Name, (Guid cursor) => db.StaffMaxMessages.Where(m => m.Id > cursor && m.CreatedAt < cutoff).OrderBy(m => m.Id),
            m => m.Id, mutate: m => db.StaffMaxMessages.Remove(m), ctx, db, ct, dateOf: m => m.CreatedAt);
    }
}
