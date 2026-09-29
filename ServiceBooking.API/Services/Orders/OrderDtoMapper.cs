using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §402 — the single mapping of an order to what the customer sees (<see cref="PublicOrderDto"/>) and
/// to what staff see (<see cref="StaffOrderDto"/>). All Russian texts come from <see cref="OrderTexts"/>. Callers load the order
/// with its Items (and Events where a journal is needed); nothing here touches the database.
/// </summary>
public sealed class OrderDtoMapper(PublicSiteLinks links)
{
    private static readonly OrderStatus[] TimelineSteps = [OrderStatus.New, OrderStatus.Accepted, OrderStatus.Ready, OrderStatus.Issued];

    /// <summary>Before issue — the estimate; after issue — the amount to pay.</summary>
    public static decimal DisplayTotal(Order order) =>
        order.Status == OrderStatus.Issued ? order.FinalTotal ?? order.EstimatedTotal : order.EstimatedTotal;

    /// <summary>A total with weight lines is an estimate until the order is issued by the actual weight.</summary>
    public static bool TotalIsApproximate(Order order) => order.HasWeightItems && order.Status != OrderStatus.Issued;

    public static decimal LineTotal(Order order, OrderItem item) =>
        order.Status == OrderStatus.Issued ? item.LineTotalFinal ?? item.LineTotalEstimated : item.LineTotalEstimated;

    public static bool LineIsApproximate(Order order, OrderItem item) =>
        item.Unit == ProductUnit.Weight && order.Status != OrderStatus.Issued;

    /// <summary>Always the four steps New → Accepted → Ready → Issued; a terminal "not issued" status stops the scale where it stopped.</summary>
    public static List<OrderTimelineStepDto> Timeline(Order order) =>
        TimelineSteps.Select(step =>
        {
            var (reached, at) = step switch
            {
                OrderStatus.New => (true, (DateTime?)order.CreatedAtUtc),
                OrderStatus.Accepted => (order.AcceptedAtUtc is not null, order.AcceptedAtUtc),
                OrderStatus.Ready => (order.ReadyAtUtc is not null, order.ReadyAtUtc),
                _ => (order.Status == OrderStatus.Issued, order.Status == OrderStatus.Issued ? order.CompletedAtUtc : null)
            };
            return new OrderTimelineStepDto(step, OrderTexts.TimelineTitle(step), reached, reached ? at : null);
        }).ToList();

    // ── Customer ─────────────────────────────────────────────────────────────────────────────────────────────────

    public PublicOrderDto ToPublic(Order order, Company shop, string? cityName)
    {
        var items = order.Items.OrderBy(i => i.Position).Select(i => new PublicOrderItemDto(
            i.NameSnapshot, i.Unit, i.UnitPrice, i.PortionTextSnapshot, i.QuantityOrdered, i.QuantityActual,
            LineTotal(order, i), LineIsApproximate(order, i))).ToList();

        // Only the shop's edits, and never the staff member's name (the customer sees "магазин", not a person).
        var shopChanges = order.Events
            .Where(e => e.Kind == OrderEventKind.Edited && e.VisibleToCustomer)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new OrderShopChangeDto(
                e.OccurredAtUtc, e.Comment, ToChangeLines(OrderChangeLog.ParseEdit(e.ChangesJson)),
                e.TotalBefore ?? 0m, e.TotalAfter ?? 0m))
            .ToList();

        return new PublicOrderDto(
            order.PublicToken, order.Number, order.BusinessDate, order.CreatedAtUtc, order.Status, OrderTexts.StatusText(order.Status),
            Timeline(order), items, DisplayTotal(order), TotalIsApproximate(order), order.Comment, order.CustomerName,
            order.CustomerPhone is null ? null : PhoneDisplayMask.Mask(order.CustomerPhone), order.StatusReason,
            OrderStateMachine.CanCustomerCancel(order.Status, order.AllowCustomerCancelSnapshot),
            ToShopInfo(shop, cityName), shopChanges, order.Version, order.CustomerKind == OrderActorKind.Guest);
    }

    public OrderShopInfoDto ToShopInfo(Company shop, string? cityName) => new(
        shop.Name, shop.Slug, links.CompanyPageUrl(shop), shop.LogoUrl, shop.Address, cityName, shop.Phone,
        shop.YandexMapsUrl, shop.TwoGisUrl);

    // ── Staff ────────────────────────────────────────────────────────────────────────────────────────────────────

    public static StaffOrderItemDto ToStaffItem(Order order, OrderItem i) => new(
        i.Id, i.ProductId, i.NameSnapshot, i.Unit, i.UnitPrice, i.PortionTextSnapshot, i.WeightStepGrams, i.QuantityOrdered,
        i.QuantityActual, LineTotal(order, i), LineIsApproximate(order, i));

    public static StaffOrderCardDto ToCard(Order order) => new(
        order.Id, order.Number, order.BusinessDate, order.CreatedAtUtc, order.Status, OrderTexts.StatusText(order.Status),
        order.CustomerName, order.CustomerPhone, order.CustomerKind, order.CustomerPhoneVerified, order.Comment,
        order.Items.OrderBy(i => i.Position).Select(i => ToStaffItem(order, i)).ToList(),
        DisplayTotal(order), TotalIsApproximate(order), order.IsModifiedByShop, order.HasWeightItems, order.StatusReason,
        order.CompletedAtUtc, order.Version, OrderStateMachine.AvailableActions(order.Status).ToList());

    public static StaffOrderDto ToStaff(Order order)
    {
        var card = ToCard(order);
        var events = order.Events.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).Select(ToEventDto).ToList();
        return new StaffOrderDto(
            card.Id, card.Number, card.BusinessDate, card.CreatedAtUtc, card.Status, card.StatusText, card.CustomerName,
            card.CustomerPhone, card.CustomerKind, card.CustomerPhoneVerified, card.Comment, card.Items, card.Total,
            card.TotalIsApproximate, card.IsModified, card.HasWeightItems, card.Reason, card.CompletedAtUtc, card.Version,
            card.AvailableActions, events);
    }

    public static OrderEventDto ToEventDto(OrderEvent e)
    {
        List<OrderChangeLineDto>? changes = e.Kind switch
        {
            OrderEventKind.Edited => ToChangeLines(OrderChangeLog.ParseEdit(e.ChangesJson)),
            OrderEventKind.Issued => ToStockLines(OrderChangeLog.ParseIssue(e.ChangesJson)),
            _ => null
        };
        return new OrderEventDto(
            e.Kind, e.OccurredAtUtc, e.ActorKind, OrderTexts.ActorName(e.ActorKind, e.ActorNameSnapshot), e.FromStatus, e.ToStatus,
            e.Reason, e.Comment, changes is { Count: > 0 } ? changes : null, e.TotalBefore, e.TotalAfter,
            OrderTexts.EventText(e.Kind, e.ToStatus, e.Reason));
    }

    // ── Change lines ─────────────────────────────────────────────────────────────────────────────────────────────

    public static List<OrderChangeLineDto> ToChangeLines(IEnumerable<ChangeEntry> entries) =>
        entries.Select(c =>
        {
            var before = c.Before is null ? null : OrderTexts.LineSummary(c.Before.Unit, c.Before.Qty, c.Before.UnitPrice);
            var after = c.After is null ? null : OrderTexts.LineSummary(c.After.Unit, c.After.Qty, c.After.UnitPrice);
            return new OrderChangeLineDto(c.Name, before, after, OrderTexts.ChangeLine(c.Name, before, after));
        }).ToList();

    private static List<OrderChangeLineDto>? ToStockLines(IssueLog? log) =>
        log?.Stock.Where(s => s.Zeroed).Select(s => new OrderChangeLineDto(
            s.Name, null, null,
            $"{s.Name}: списано {OrderTexts.Quantity(s.Unit, s.Written)} из {OrderTexts.Quantity(s.Unit, s.Requested)}, остаток обнулён")).ToList();
}
