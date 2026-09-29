using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §458 — the ONE place that turns "what happened to an order" into notifications. <see cref="OrderEventLog"/> — the
/// single writer of the journal — calls <see cref="OnEventAsync"/> at the end of every append, so a transition that forgets to notify
/// cannot exist. What to send is the pure <see cref="OrderNotificationPlan"/> (a table); here only the parts that need the database are done:
/// who the staff are, which browsers subscribed, which channels the shop has. Only rows are ADDED to the caller's transaction, SaveChanges is
/// the caller's — and there are no network calls (a broken push service cannot break an order action).
/// The recipients of staff notifications are chosen here and nowhere else, which is where a future "staff chat in MAX" target (cycle 25) joins.
/// </summary>
public sealed class OrderNotificationPlanner(
    AppDbContext db, OrderStaffPushQueue staffQueue, CustomerOrderPushQueue customerQueue, OrderMessageScheduler messenger)
{
    public async Task OnEventAsync(Order order, OrderEvent orderEvent, CancellationToken ct = default)
    {
        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == order.CompanyId, ct)
                       ?? new ShopSettings { CompanyId = order.CompanyId };
        var notificationSettings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == order.CompanyId, ct);
        var flags = new OrderNotificationFlags(
            notificationSettings?.StaffPushEnabled ?? new CompanyNotificationSettings().StaffPushEnabled,
            settings.CustomerWebPushEnabled, settings.CustomerMessengerEnabled, order.NotifyByMessenger);

        var plan = OrderNotificationPlan.For(orderEvent.Kind, orderEvent.ToStatus, flags);
        if (plan.IsEmpty) return;

        var shop = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == order.CompanyId, ct);
        var facts = BuildFacts(order, orderEvent, shop);

        if (plan.StaffType is { } staffType)
        {
            var payload = staffType == NotificationType.StaffOrderCancelledByCustomer
                ? OrderNotificationTexts.StaffOrderCancelledByCustomer(facts, shop.Id, order.Id)
                : OrderNotificationTexts.StaffOrderCreated(facts, shop.Id, order.Id);
            await staffQueue.QueueForStaffAsync(order, orderEvent.Id, staffType, payload, ct);
        }

        if (plan.CustomerType is { } customerType)
        {
            if (plan.CustomerWebPush)
                await customerQueue.QueueAsync(order, orderEvent.Id, customerType,
                    OrderNotificationTexts.CustomerWebPush(customerType, facts, order.Id, order.PublicToken), ct);
            if (plan.CustomerMessenger)
                await messenger.QueueAsync(order, shop, orderEvent.Id, customerType, facts, ct);
        }
    }

    /// <summary>The facts every text needs, from what the caller already loaded (no customer name or phone — [legal L10, L11]).</summary>
    public static OrderTextFacts BuildFacts(Order order, OrderEvent orderEvent, Company shop)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
        var start = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(order.PickupStartUtc), zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        int? previousNumber = orderEvent.Kind == OrderEventKind.PickupChanged
            ? OrderChangeLog.ParsePickup(orderEvent.ChangesJson)?.Before.Number
            : null;
        return new OrderTextFacts(
            shop.Name, order.Number, order.PickupKind, order.PickupDate, start.Hour * 60 + start.Minute, today,
            order.Items.Count, OrderDtoMapper.DisplayTotal(order), orderEvent.Reason, previousNumber);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
