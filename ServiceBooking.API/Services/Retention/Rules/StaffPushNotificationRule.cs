using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>ARCHITECTURE_CYCLE9.md §105.11. Deletes a <see cref="Core.Entities.StaffPushNotification"/>
/// queue/journal row older than the cutoff, outright — unlike <c>NotificationBodyRedactionRule</c>'s
/// two-step redact-then-delete for <see cref="Core.Entities.OutboundNotification"/>, there is no separate
/// redaction step here: <c>Payload</c> is a one-shot system message ("Новая запись: ..."), not
/// correspondence worth a scrubbed trace of for a year first.</summary>
public sealed class StaffPushNotificationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "staff-push-notification";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).StaffPushNotification;

        IQueryable<Core.Entities.StaffPushNotification> Query(Guid cursor) => db.StaffPushNotifications
            .Where(n => n.Id > cursor && n.CreatedAt < cutoff)
            .OrderBy(n => n.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, n => n.Id,
            mutate: n => db.StaffPushNotifications.Remove(n),
            ctx, db, ct,
            dateOf: n => n.CreatedAt);
    }
}
