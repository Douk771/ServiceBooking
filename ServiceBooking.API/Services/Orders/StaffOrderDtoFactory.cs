using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §453, §481 — builds what staff see of an order, in ONE place: the pickup as text, the "overdue" mark at the moment of
/// the answer, and (P1, US-24-23) the status of the last messenger message about the order. The last message of MANY orders comes from one query.
/// </summary>
public class StaffOrderDtoFactory(AppDbContext db)
{
    public async Task<StaffOrderDto> BuildAsync(Order order, Company shop, CancellationToken ct = default)
    {
        var statuses = order.NotifyByMessenger ? await MessengerStatusesAsync([order.Id], ct) : [];
        return OrderDtoMapper.ToStaff(order, OrderPickupContext.For(shop, DateTime.UtcNow), statuses.GetValueOrDefault(order.Id));
    }

    public async Task<List<StaffOrderCardDto>> BuildCardsAsync(IReadOnlyCollection<Order> orders, OrderPickupContext ctx, CancellationToken ct = default)
    {
        var ids = orders.Where(o => o.NotifyByMessenger).Select(o => o.Id).ToList();
        var statuses = ids.Count == 0 ? [] : await MessengerStatusesAsync(ids, ct);
        return orders.Select(o => OrderDtoMapper.ToCard(o, ctx, statuses.GetValueOrDefault(o.Id))).ToList();
    }

    /// <summary>The newest delivery-journal row per order → "доставлено", "не доставлено: у клиента нет MAX", …; no row yet = the order asked for messages but none was queued.</summary>
    private async Task<Dictionary<Guid, OrderMessengerStatusDto>> MessengerStatusesAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct)
    {
        var rows = await db.OutboundNotifications.AsNoTracking()
            .Where(n => n.OrderId != null && orderIds.Contains(n.OrderId.Value))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { OrderId = n.OrderId!.Value, n.Status, n.Reason, n.ChannelId, n.ReadAtUtc, n.AttemptCount, n.LastAttemptAtUtc, n.SentAtUtc })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.OrderId).ToDictionary(
            g => g.Key,
            g =>
            {
                var last = g.First();
                return new OrderMessengerStatusDto(
                    true, last.Status, NotificationTexts.StatusText(last.Status, last.Reason, last.ChannelId, last.ReadAtUtc, last.AttemptCount),
                    last.LastAttemptAtUtc ?? last.SentAtUtc);
            });
    }
}
