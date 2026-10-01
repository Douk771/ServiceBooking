using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.6 — the ONLY writer of <c>OrderEvents</c> (the BookingEventLog pattern), and the ONLY
/// writer of <c>ShopSettings.OrdersRevision</c>. Every change of an order calls <see cref="AppendAsync"/> inside the
/// change's own transaction, BEFORE SaveChanges (and hands the event to <see cref="OrderNotificationPlanner"/>), so "the order changed but the journal/revision did not" is impossible by
/// construction — which is also why the board's cheap revision poll cannot miss a change (§388.4-6).
///
/// It adds the journal row to the tracked graph (the caller's SaveChanges persists it with the order) and bumps the
/// revision with one upsert statement. Callers must NOT wrap it in try/catch: a journal that silently misses a change is
/// worse than a failed request. The revision statement is a row-level write on the shop's settings row, held until the
/// transaction ends — order changes of one shop are serialized on it, which at ~500 orders a day is invisible.
/// The optional <c>occurredAtUtc</c> exists for the demo stand's board task alone (§35.10.3); the writer stays one.
/// </summary>
public class OrderEventLog(AppDbContext db, OrderNotificationPlanner planner)
{
    public async Task AppendAsync(
        Order order, OrderEventKind kind, OrderActor actor, OrderStatus? fromStatus, OrderStatus? toStatus,
        string? reason = null, string? comment = null, string? changesJson = null,
        decimal? totalBefore = null, decimal? totalAfter = null, bool visibleToCustomer = true, DateTime? occurredAtUtc = null)
    {
        var orderEvent = new OrderEvent
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            CompanyId = order.CompanyId,
            Kind = kind,
            // Only the demo board task (ARCHITECTURE_CYCLE35.md §35.10.3) passes a moment: the planned one of the generator's timeline, not "now". Every product call
            // leaves it null, so nothing else changes. The journal keeps exactly ONE writer.
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            ActorKind = actor.Kind,
            ActorUserId = actor.UserId,
            ActorNameSnapshot = actor.NameSnapshot,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Reason = reason,
            Comment = comment,
            ChangesJson = changesJson,
            TotalBefore = totalBefore,
            TotalAfter = totalAfter,
            VisibleToCustomer = visibleToCustomer,
        };
        db.OrderEvents.Add(orderEvent);

        // Upsert: a shop whose settings row is missing (readers treat that as "defaults") still gets a counter.
        // The literals are the column defaults of ShopSettings (Anyone = 0, Manual = 0, AllowCustomerCancel, no stock tracking).
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ShopSettings" ("CompanyId", "CustomerMode", "AcceptanceMode", "AllowCustomerCancel", "TrackStock", "OrdersRevision", "UpdatedAtUtc")
            VALUES ({order.CompanyId}, 0, 0, TRUE, FALSE, 1, now() at time zone 'utc')
            ON CONFLICT ("CompanyId") DO UPDATE SET "OrdersRevision" = "ShopSettings"."OrdersRevision" + 1
            """);

        // ARCHITECTURE_CYCLE24.md §458 — the single point that decides who is told what (push to staff, web-push and messenger to the
        // customer). Rows are only added to this transaction; a failure here fails the action, like a failing journal row would.
        await planner.OnEventAsync(order, orderEvent);
    }
}
