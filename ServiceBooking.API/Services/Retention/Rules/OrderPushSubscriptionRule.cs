using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §456.4 [legal L16]. Deletes the browser subscriptions of an ORDER (<see cref="OrderPushSubscription"/>) once the order has been in a
/// final status for <see cref="RetentionPeriods.OrderPushSubscriptionDays"/> (default 7): after the final status nothing is sent through them, so keeping
/// the endpoint of a customer's browser has no purpose. A technical term, not a legal conclusion — legal-counsel may change it in configuration. An order
/// that is still active keeps its subscriptions. Deletes outright (there is no personal data to scrub and nothing to keep).
/// </summary>
public sealed class OrderPushSubscriptionRule(AppDbContext db) : IRetentionRule
{
    public string Name => "order-push-subscriptions";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = ctx.NowUtc.AddDays(-ctx.Periods.OrderPushSubscriptionDays);

        IQueryable<OrderPushSubscription> Query(Guid cursor) => db.OrderPushSubscriptions
            .Where(s => s.Id > cursor && s.Order.CompletedAtUtc != null && s.Order.CompletedAtUtc < cutoff)
            .OrderBy(s => s.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, s => s.Id,
            mutate: s => db.OrderPushSubscriptions.Remove(s),
            ctx, db, ct,
            dateOf: s => s.CreatedAtUtc);
    }
}
