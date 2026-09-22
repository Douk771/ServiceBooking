using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.3, API_CONTRACT_CYCLE5.md §51). Redacts (not deletes) a
/// terminal <see cref="Core.Entities.OutboundNotification"/>'s personal-data fields once it is older than
/// <see cref="RetentionPeriods.NotificationBodyDays"/> — the row itself stays a permanent delivery-journal
/// entry (Status/Reason/dates untouched), which is exactly why <see cref="Core.Entities.OutboundNotification.ContentRedactedAtUtc"/>
/// (added in T5-B11, unused until now) exists: it is the field the export endpoint and
/// <c>GET .../notifications</c> (§51) read as <c>contentRedacted</c>/<c>bodyAvailable</c>.
/// </summary>
public sealed class NotificationBodyRedactionRule(AppDbContext db) : IRetentionRule
{
    public string Name => "notification-body";

    private static readonly NotificationStatus[] TerminalStatuses =
    [
        NotificationStatus.Sent, NotificationStatus.Delivered, NotificationStatus.Failed,
        NotificationStatus.Expired, NotificationStatus.Skipped, NotificationStatus.Cancelled,
    ];

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).NotificationBody;

        // §49.3's exact predicate, translated to LINQ: ContentRedactedAtUtc IS NULL, terminal status,
        // COALESCE(SentAtUtc, LastAttemptAtUtc, DueAtUtc) < cutoff. Cursor (Id > lastSeenId) is layered on
        // top for paging — see RetentionRuleRunner's doc comment for why it must not depend on
        // ContentRedactedAtUtc having actually been persisted.
        IQueryable<Core.Entities.OutboundNotification> Query(Guid cursor) => db.OutboundNotifications
            .Where(n => n.Id > cursor
                        && n.ContentRedactedAtUtc == null
                        && TerminalStatuses.Contains(n.Status)
                        && (n.SentAtUtc ?? n.LastAttemptAtUtc ?? n.DueAtUtc) < cutoff)
            .OrderBy(n => n.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, n => n.Id,
            mutate: n =>
            {
                n.Body = string.Empty;
                n.RecipientName = null;
                n.RecipientPhone = string.Empty;
                n.ContentRedactedAtUtc = ctx.NowUtc;
            },
            ctx, db, ct,
            dateOf: n => n.SentAtUtc ?? n.LastAttemptAtUtc ?? n.DueAtUtc);
    }
}
