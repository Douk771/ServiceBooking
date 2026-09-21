using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9. §49.3 explicitly rejects moving <see cref="Core.Entities.OutboundNotification"/> metadata
/// into a separate aggregate ("UPDATE на месте, а не перенос метаданных в агрегат") — the row is the
/// journal. LEGAL_REVIEW.md §13.5's "1 год, затем агрегат" is therefore read here as: once a row has
/// already been through <see cref="NotificationBodyRedactionRule"/> (no personal data left in it at all —
/// Body/RecipientName/RecipientPhone are already wiped) AND is additionally older than
/// <see cref="RetentionPeriods.NotificationMetadataDays"/>, nothing in the product reads it any more and
/// the row itself is removed. The <c>ContentRedactedAtUtc IS NOT NULL</c> guard is a deliberate ordering
/// invariant: a row can only ever become eligible for deletion AFTER it has already been redacted, never
/// before — this rule can never be the first thing to touch a row that still has personal data in it.
/// </summary>
public sealed class NotificationMetadataDeletionRule(AppDbContext db) : IRetentionRule
{
    public string Name => "notification-metadata";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).NotificationMetadata;

        IQueryable<Core.Entities.OutboundNotification> Query(Guid cursor) => db.OutboundNotifications
            .Where(n => n.Id > cursor && n.ContentRedactedAtUtc != null && n.ContentRedactedAtUtc < cutoff)
            .OrderBy(n => n.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, n => n.Id,
            mutate: n => db.OutboundNotifications.Remove(n),
            ctx, db, ct,
            dateOf: n => n.ContentRedactedAtUtc!.Value);
    }
}
