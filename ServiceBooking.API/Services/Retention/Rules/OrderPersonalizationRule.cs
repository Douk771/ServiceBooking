using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §398.5. Depersonalizes a finished order (any terminal status) once it is older than
/// <see cref="RetentionPeriods.OrderPersonalDataDays"/>, counted from <c>CompletedAtUtc</c> — exactly the fields the
/// account-deletion path erases (<see cref="OrderPersonalData"/>), plus the customer's own journal entries. The number, lines, totals
/// and status stay: the shop's books must survive.
///
/// ⚠️ The committed configuration is 0, which this rule reads as "срок не задан" (legal-counsel has not concluded, §404 L5): at 0 it
/// deletes/changes NOTHING and says so in its summary line — the BookingEventRule precedent. Deliberately NOT a startup fail-fast: the
/// minimum is the open legal question, and refusing to start over it would block the release on someone else's timeline.
/// When legal gives a number, changing <c>Retention:OrderPersonalDataDays</c> is the whole fix. Dry-run is the task's common
/// <c>DryRun: true</c> (same selection query, no SaveChanges).
/// </summary>
public sealed class OrderPersonalizationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "order-personalization";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        if (ctx.Periods.OrderPersonalDataDays <= 0)
        {
            var summary = $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено";
            return Task.FromResult(new RetentionOutcome(Name, Scanned: 0, Affected: 0, summary) { Skipped = true });
        }

        var cutoff = ctx.NowUtc.AddDays(-ctx.Periods.OrderPersonalDataDays);

        // Terminal orders only (an active order is still being fulfilled), not yet erased, completed before the cutoff.
        IQueryable<Order> Query(Guid cursor) => db.Orders.Include(o => o.Events)
            .Where(o => o.Id > cursor && !o.PersonalDataErased && o.CompletedAtUtc != null && o.CompletedAtUtc < cutoff &&
                        o.Status != OrderStatus.New && o.Status != OrderStatus.Accepted && o.Status != OrderStatus.Ready)
            .OrderBy(o => o.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, o => o.Id,
            mutate: o =>
            {
                OrderPersonalData.Erase(o);
                foreach (var orderEvent in o.Events) OrderPersonalData.TombstoneCustomerEvent(orderEvent);
            },
            ctx, db, ct,
            dateOf: o => o.CompletedAtUtc!.Value);
    }
}
