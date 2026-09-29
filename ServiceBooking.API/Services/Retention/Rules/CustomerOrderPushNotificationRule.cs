using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §456.4 [legal L16]. Deletes a <see cref="CustomerOrderPushNotification"/> queue/journal row older than
/// <see cref="RetentionPeriods.CustomerOrderPushNotificationDays"/> (default 90) since it was created — outright, like
/// <see cref="StaffPushNotificationRule"/>: its payload is a one-shot status message with no name and no phone.
/// </summary>
public sealed class CustomerOrderPushNotificationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "customer-order-push-notifications";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = ctx.NowUtc.AddDays(-ctx.Periods.CustomerOrderPushNotificationDays);

        IQueryable<CustomerOrderPushNotification> Query(Guid cursor) => db.CustomerOrderPushNotifications
            .Where(n => n.Id > cursor && n.CreatedAt < cutoff)
            .OrderBy(n => n.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, n => n.Id,
            mutate: n => db.CustomerOrderPushNotifications.Remove(n),
            ctx, db, ct,
            dateOf: n => n.CreatedAt);
    }
}
