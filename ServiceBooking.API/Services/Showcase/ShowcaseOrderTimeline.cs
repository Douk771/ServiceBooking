using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>How a generated order that reaches its pickup time ends: handed over, or not collected (closed 45 minutes after the start of the slot).</summary>
public enum ShowcaseOrderFate
{
    Issued,
    NotPickedUp,
}

/// <summary>
/// The moments at which a generated order changes status, all derived from the order itself (ARCHITECTURE_CYCLE35.md §35.10.2). The same plan is read twice:
/// by the generator at the moment of the reset, and by the demo task <c>demo-board-tick</c> during the day, so what the task does later is exactly what
/// the generator would have written had the reset happened later.
/// </summary>
public sealed record ShowcaseOrderPlan(
    Guid OrderId, OrderAcceptanceMode Mode, DateTime CreatedAtUtc, DateTime PickupStartUtc,
    DateTime AcceptedAtUtc, DateTime ReadyAtUtc, DateTime ClosedAtUtc, ShowcaseOrderFate Fate)
{
    /// <summary>The status an order has right after it was created: <c>New</c>, or <c>Accepted</c> in the automatic acceptance mode.</summary>
    public OrderStatus InitialStatus => Mode == OrderAcceptanceMode.Auto ? OrderStatus.Accepted : OrderStatus.New;

    public OrderStatus FinalStatus => Fate == ShowcaseOrderFate.Issued ? OrderStatus.Issued : OrderStatus.NotPickedUp;
}

/// <summary>One transition of a planned order: which journal entry it is, between which statuses, and when.</summary>
public sealed record ShowcaseOrderStep(OrderEventKind Kind, OrderStatus From, OrderStatus To, DateTime AtUtc);

/// <summary>The status of a planned order at a given moment and the transitions that happened on the way (in order, each not later than the moment).</summary>
public sealed record ShowcaseOrderState(OrderStatus Status, IReadOnlyList<ShowcaseOrderStep> Steps);

/// <summary>
/// ARCHITECTURE_CYCLE35.md §35.10.2 (A35-5) — the "live" board of the demo as two pure functions, without EF and without a clock:
/// <see cref="Plan"/> fixes the moments from <c>(order id, creation, pickup start, acceptance mode)</c>, <see cref="StateAt"/> cuts the plan at a moment.
///
/// The offsets come from <see cref="ShowcaseRandom"/> seeded by the order id, so they never change between resets and machines:
/// accepted 30–50 minutes before the pickup start (an order placed a long time ahead, more than 12 hours, is accepted early instead: 5–240 minutes after
/// creation, but not later than an hour before the pickup), ready 5–15 minutes before it, handed over 3–20 minutes after it; 5 % of orders are not collected
/// and closed 45 minutes after the start. Every moment is after the previous one by at least a minute.
/// </summary>
public static class ShowcaseOrderTimeline
{
    /// <summary>An order placed further ahead than this is accepted by the shop soon after creation rather than just before the pickup.</summary>
    public static readonly TimeSpan EarlyAcceptanceGap = TimeSpan.FromHours(12);

    public const double NotPickedUpShare = 0.05;
    public const int NotPickedUpAfterMinutes = 45;

    /// <param name="orderId">The id of the order: the seed of every offset.</param>
    /// <param name="createdAtUtc">When the order was created.</param>
    /// <param name="pickupStartUtc">The start of the pickup slot, or the estimate of an "as soon as possible" order.</param>
    /// <param name="mode">The acceptance mode of the shop at creation: in <c>Auto</c> the order is born accepted.</param>
    /// <param name="forcedFate">The generator forces the end of a PAST order (88 % handed over, 3 % not collected, ARCHITECTURE_CYCLE35.md §35.9.3); the demo task never
    /// does, so the plan it reads from an order of today is the plan the generator wrote.</param>
    public static ShowcaseOrderPlan Plan(
        Guid orderId, DateTime createdAtUtc, DateTime pickupStartUtc, OrderAcceptanceMode mode, ShowcaseOrderFate? forcedFate = null)
    {
        // The draws are made in a fixed order whatever the branch, so a change of one branch never shifts the numbers of another.
        var rng = new ShowcaseRandom($"order-timeline:{orderId:N}");
        var shortAcceptLead = rng.Next(30, 51);
        var earlyAcceptDelay = rng.Next(5, 241);
        var acceptJitter = rng.Next(1, 4);
        var readyLead = rng.Next(5, 16);
        var issueDelay = rng.Next(3, 21);
        var drawnFate = rng.Chance(NotPickedUpShare) ? ShowcaseOrderFate.NotPickedUp : ShowcaseOrderFate.Issued;
        var fate = forcedFate ?? drawnFate;

        DateTime accepted;
        if (mode == OrderAcceptanceMode.Auto)
        {
            accepted = createdAtUtc;
        }
        else if (pickupStartUtc - createdAtUtc > EarlyAcceptanceGap)
        {
            var early = createdAtUtc.AddMinutes(earlyAcceptDelay);
            var latest = pickupStartUtc.AddMinutes(-60);
            accepted = Max(createdAtUtc.AddMinutes(1), early < latest ? early : latest);
        }
        else
        {
            accepted = Max(createdAtUtc.AddMinutes(acceptJitter), pickupStartUtc.AddMinutes(-shortAcceptLead));
        }

        var ready = Max(accepted.AddMinutes(2), pickupStartUtc.AddMinutes(-readyLead));
        var closed = Max(ready.AddMinutes(1),
            fate == ShowcaseOrderFate.Issued ? pickupStartUtc.AddMinutes(issueDelay) : pickupStartUtc.AddMinutes(NotPickedUpAfterMinutes));
        return new ShowcaseOrderPlan(orderId, mode, createdAtUtc, pickupStartUtc, accepted, ready, closed, fate);
    }

    /// <summary>All transitions of the plan, in order, regardless of the moment.</summary>
    public static IReadOnlyList<ShowcaseOrderStep> AllSteps(ShowcaseOrderPlan plan)
    {
        var steps = new List<ShowcaseOrderStep>(3);
        if (plan.Mode != OrderAcceptanceMode.Auto)
            steps.Add(new ShowcaseOrderStep(OrderEventKind.Accepted, OrderStatus.New, OrderStatus.Accepted, plan.AcceptedAtUtc));
        steps.Add(new ShowcaseOrderStep(OrderEventKind.MarkedReady, OrderStatus.Accepted, OrderStatus.Ready, plan.ReadyAtUtc));
        steps.Add(plan.Fate == ShowcaseOrderFate.Issued
            ? new ShowcaseOrderStep(OrderEventKind.Issued, OrderStatus.Ready, OrderStatus.Issued, plan.ClosedAtUtc)
            : new ShowcaseOrderStep(OrderEventKind.NotPickedUp, OrderStatus.Ready, OrderStatus.NotPickedUp, plan.ClosedAtUtc));
        return steps;
    }

    /// <summary>The state of the plan at <paramref name="nowUtc"/>: the transitions that already happened (moment ≤ now) and the status after the last of them.</summary>
    public static ShowcaseOrderState StateAt(ShowcaseOrderPlan plan, DateTime nowUtc)
    {
        var done = AllSteps(plan).Where(s => s.AtUtc <= nowUtc).ToList();
        return new ShowcaseOrderState(done.Count == 0 ? plan.InitialStatus : done[^1].To, done);
    }

    private static DateTime Max(DateTime a, DateTime b) => a >= b ? a : b;
}
