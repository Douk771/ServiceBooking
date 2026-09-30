using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Demo;

/// <summary>A visitor's order the demo closes by itself (branch B): the journal kind and the status it ends in.</summary>
public sealed record DemoVisitorClosure(OrderEventKind Kind, OrderStatus To);

/// <summary>
/// ARCHITECTURE_CYCLE35.md §35.10.3 — the pure rules of the demo board task <c>demo-board-tick</c>, without EF and without a clock: which orders are the generator's, which
/// transitions of the generator's plan are due, what happens to a visitor's order whose pickup time has long passed. Everything the task decides is here, so the unit tests
/// (<c>DemoBoardTickRulesTests</c>) cover the decisions and the functional ones (QA) only the plumbing.
/// </summary>
public static class DemoBoardTickRules
{
    /// <summary>The reason the customer sees on an order the demo closed (API_CONTRACT_CYCLE35.md §35.27).</summary>
    public const string VisitorClosedReason = "Демо: заказ закрыт автоматически — время получения прошло.";

    /// <summary>A visitor's order is closed once its pickup time (the end of the slot, or the estimate of an "as soon as possible" order) has passed by more than this.</summary>
    public static readonly TimeSpan VisitorOrderGrace = TimeSpan.FromMinutes(50);

    /// <summary>The generator writes orders created before the moment of the reset; a visitor can create one only after its commit (the API answers 503 meanwhile), so the
    /// creation time alone tells the two apart — the demo has no other mark on an order (§35.3.1).</summary>
    public static bool IsGeneratorOrder(DateTime createdAtUtc, DateTime lastResetUtc) => createdAtUtc <= lastResetUtc;

    /// <summary>The handover needs the actual weights and writes the stock off, which the task does not invent: an order with weight lines or lines reserving stock waits for
    /// a visitor (the coffee shop has none by construction).</summary>
    public static bool CanIssue(bool hasWeightItems, bool anyLineReservesStock) => !hasWeightItems && !anyLineReservesStock;

    /// <summary>
    /// The transitions of the generator's plan that are due at <paramref name="nowUtc"/> for an order that is now in <paramref name="current"/>, in order. A step is taken only when
    /// it starts in the status the order is in: if a visitor has already moved the order (handed it over, refused, cancelled, or accepted it himself) the steps that no longer
    /// fit are skipped, and an order out of the plan's chain gets none. The first step that cannot be made (a handover of an order that <see cref="CanIssue"/> forbids) ends the list.
    /// </summary>
    public static IReadOnlyList<ShowcaseOrderStep> DueSteps(OrderStatus current, ShowcaseOrderPlan plan, DateTime nowUtc, bool canIssue)
    {
        var due = new List<ShowcaseOrderStep>(3);
        var cursor = current;
        foreach (var step in ShowcaseOrderTimeline.StateAt(plan, nowUtc).Steps)
        {
            if (step.From != cursor) continue;
            if (step.Kind == OrderEventKind.Issued && !canIssue) break;
            due.Add(step);
            cursor = step.To;
        }
        return due;
    }

    /// <summary>Whether the moment of a step may be used as written: it must not be earlier than the last change of the order (a visitor's edit of a moment ago), or the journal
    /// would run backwards. Returns the moment to write.</summary>
    public static DateTime JournalMoment(DateTime plannedAtUtc, DateTime orderUpdatedAtUtc) => plannedAtUtc >= orderUpdatedAtUtc ? plannedAtUtc : orderUpdatedAtUtc;

    /// <summary>
    /// Branch B (US-35-09): what the demo does with a visitor's active order whose pickup time passed by more than <see cref="VisitorOrderGrace"/> — New is refused, Accepted is
    /// cancelled by the shop, Ready is marked "not picked up". Null while it is not yet time, and for a status that is not active.
    /// </summary>
    public static DemoVisitorClosure? VisitorClosure(OrderStatus status, DateTime pickupStartUtc, DateTime? pickupEndUtc, DateTime nowUtc)
    {
        if ((pickupEndUtc ?? pickupStartUtc) + VisitorOrderGrace >= nowUtc) return null;
        return status switch
        {
            OrderStatus.New => new DemoVisitorClosure(OrderEventKind.Rejected, OrderStatus.Rejected),
            OrderStatus.Accepted => new DemoVisitorClosure(OrderEventKind.CancelledByShop, OrderStatus.CancelledByShop),
            OrderStatus.Ready => new DemoVisitorClosure(OrderEventKind.NotPickedUp, OrderStatus.NotPickedUp),
            _ => null,
        };
    }
}
