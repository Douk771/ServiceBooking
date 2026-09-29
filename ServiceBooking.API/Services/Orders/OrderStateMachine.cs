using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.1 — the order status machine as a pure function. The frontend never computes
/// which buttons to show: it prints <see cref="AvailableActions"/>. Terminal statuses have no way out (no rollback).
/// </summary>
public static class OrderStateMachine
{
    private static readonly OrderAction[] None = [];
    private static readonly OrderAction[] ForNew = [OrderAction.Accept, OrderAction.Reject, OrderAction.Edit];
    private static readonly OrderAction[] ForAccepted = [OrderAction.MarkReady, OrderAction.Cancel, OrderAction.Edit];
    private static readonly OrderAction[] ForReady = [OrderAction.Issue, OrderAction.NotPickedUp, OrderAction.Cancel, OrderAction.Edit];

    /// <summary>New, Accepted, Ready — the order still holds its stock reserve and lives on the board.</summary>
    public static bool IsActive(OrderStatus status) => status is OrderStatus.New or OrderStatus.Accepted or OrderStatus.Ready;

    public static bool IsTerminal(OrderStatus status) => !IsActive(status);

    public static IReadOnlyList<OrderAction> AvailableActions(OrderStatus status) => status switch
    {
        OrderStatus.New => ForNew,
        OrderStatus.Accepted => ForAccepted,
        OrderStatus.Ready => ForReady,
        _ => None
    };

    public static bool IsAllowed(OrderStatus status, OrderAction action) => AvailableActions(status).Contains(action);

    /// <summary>
    /// The status after a staff action. <see cref="OrderAction.Edit"/> keeps the status. Returns false when the
    /// action is not available in <paramref name="from"/> (the caller answers 409 InvalidTransition).
    /// </summary>
    public static bool TryApply(OrderStatus from, OrderAction action, out OrderStatus to)
    {
        to = from;
        if (!IsAllowed(from, action)) return false;
        to = action switch
        {
            OrderAction.Accept => OrderStatus.Accepted,
            OrderAction.Reject => OrderStatus.Rejected,
            OrderAction.MarkReady => OrderStatus.Ready,
            OrderAction.Issue => OrderStatus.Issued,
            OrderAction.NotPickedUp => OrderStatus.NotPickedUp,
            OrderAction.Cancel => OrderStatus.CancelledByShop,
            _ => from
        };
        return true;
    }

    /// <summary>Customer cancellation by link: only New/Accepted, and only if the order's own snapshot allows it.</summary>
    public static bool CanCustomerCancel(OrderStatus status, bool allowCustomerCancelSnapshot) =>
        allowCustomerCancelSnapshot && (status is OrderStatus.New or OrderStatus.Accepted);
}
