using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §508 [legal L20] — two things, one rule. (1) A staff MAX link that the bot's stop switched off
/// (<see cref="StaffMaxLinkStatus.StoppedInMax"/>, chat id already erased) is kept <see cref="RetentionPeriods.StaffMaxStoppedLinkDays"/> only so the
/// cabinet can say "Отключено: бот остановлен в MAX", then deleted. (2) A link session is deleted <see cref="RetentionPeriods.StaffMaxLinkSessionDays"/> after
/// it expired. An ACTIVE link is never touched: it lives as long as the person wants it. Dry-run is the task's common one (same queries, no SaveChanges).
/// </summary>
public sealed class StaffMaxLinkRule(AppDbContext db) : IRetentionRule
{
    public string Name => "staff-max-links";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var stoppedCutoff = ctx.NowUtc.AddDays(-Math.Max(1, ctx.Periods.StaffMaxStoppedLinkDays));
        var sessionCutoff = ctx.NowUtc.AddDays(-Math.Max(1, ctx.Periods.StaffMaxLinkSessionDays));

        var links = await RetentionRuleRunner.RunAsync(
            Name, (Guid cursor) => db.StaffMaxLinks
                .Where(l => l.Id > cursor && l.Status == StaffMaxLinkStatus.StoppedInMax && l.StoppedAtUtc != null && l.StoppedAtUtc < stoppedCutoff)
                .OrderBy(l => l.Id),
            l => l.Id, mutate: l => db.StaffMaxLinks.Remove(l), ctx, db, ct, dateOf: l => l.StoppedAtUtc!.Value);

        var sessions = await RetentionRuleRunner.RunAsync(
            Name, (Guid cursor) => db.StaffMaxLinkSessions.Where(s => s.Id > cursor && s.ExpiresAtUtc < sessionCutoff).OrderBy(s => s.Id),
            s => s.Id, mutate: s => db.StaffMaxLinkSessions.Remove(s), ctx, db, ct, dateOf: s => s.ExpiresAtUtc);

        var scanned = links.Scanned + sessions.Scanned;
        var affected = links.Affected + sessions.Affected;
        return new RetentionOutcome(Name, scanned, affected,
            $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: scanned={scanned} affected={affected} (stopped links={links.Affected}, sessions={sessions.Affected})");
    }
}
