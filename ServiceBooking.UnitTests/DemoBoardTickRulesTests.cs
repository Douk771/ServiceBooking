using FluentAssertions;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE35.md §35.10.3 — the pure decisions of the demo board task: which orders are the generator's, which transitions are due, what happens to a visitor's order.</summary>
public class DemoBoardTickRulesTests
{
    private static readonly DateTime Pickup = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Created = Pickup.AddHours(-3);
    private static readonly ShowcaseOrderPlan Plan = ShowcaseOrderTimeline.Plan(
        ShowcaseIds.For("demo", "order", "tick-rules"), Created, Pickup, OrderAcceptanceMode.Manual, ShowcaseOrderFate.Issued);

    // ── Whose order ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnOrderCreatedAtOrBeforeTheResetIsTheGenerators_OneCreatedAfterItIsAVisitors()
    {
        var reset = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

        DemoBoardTickRules.IsGeneratorOrder(reset.AddMinutes(-1), reset).Should().BeTrue();
        DemoBoardTickRules.IsGeneratorOrder(reset, reset).Should().BeTrue();
        DemoBoardTickRules.IsGeneratorOrder(reset.AddSeconds(1), reset).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void TheHandoverIsMadeOnlyForPiecesWithoutStock(bool weight, bool stock, bool expected) =>
        DemoBoardTickRules.CanIssue(weight, stock).Should().Be(expected);

    // ── The generator's plan ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NothingIsDueBeforeTheFirstMoment() =>
        DemoBoardTickRules.DueSteps(OrderStatus.New, Plan, Plan.AcceptedAtUtc.AddSeconds(-1), canIssue: true).Should().BeEmpty();

    [Fact]
    public void AnOrderIsCaughtUpStepByStep_WhateverTheDelayOfThePass()
    {
        // A new order, and a pass that comes only when the whole plan is over (the container was stopped): all three transitions in one go, in order.
        var all = DemoBoardTickRules.DueSteps(OrderStatus.New, Plan, Plan.ClosedAtUtc.AddHours(1), canIssue: true);
        all.Select(s => s.To).Should().Equal(OrderStatus.Accepted, OrderStatus.Ready, OrderStatus.Issued);
        all.Select(s => s.AtUtc).Should().Equal(Plan.AcceptedAtUtc, Plan.ReadyAtUtc, Plan.ClosedAtUtc);

        // An ordinary pass just after the handover time of an order that is Ready: only the handover.
        var last = DemoBoardTickRules.DueSteps(OrderStatus.Ready, Plan, Plan.ClosedAtUtc.AddSeconds(30), canIssue: true);
        last.Should().ContainSingle().Which.Kind.Should().Be(OrderEventKind.Issued);

        DemoBoardTickRules.DueSteps(OrderStatus.Accepted, Plan, Plan.ReadyAtUtc.AddSeconds(1), canIssue: true)
            .Should().ContainSingle().Which.To.Should().Be(OrderStatus.Ready);
    }

    [Fact]
    public void AnOrderAVisitorHasAlreadyMovedIsNotTouched_WhateverIsDue()
    {
        var late = Plan.ClosedAtUtc.AddHours(5);

        foreach (var status in new[] { OrderStatus.Issued, OrderStatus.Rejected, OrderStatus.CancelledByCustomer, OrderStatus.CancelledByShop, OrderStatus.NotPickedUp })
            DemoBoardTickRules.DueSteps(status, Plan, late, canIssue: true).Should().BeEmpty(status.ToString());
    }

    [Fact]
    public void AnOrderAVisitorAcceptedHimself_ContinuesFromThere_WithoutAcceptingAgain()
    {
        var steps = DemoBoardTickRules.DueSteps(OrderStatus.Accepted, Plan, Plan.ClosedAtUtc.AddHours(1), canIssue: true);

        steps.Select(s => s.Kind).Should().Equal(OrderEventKind.MarkedReady, OrderEventKind.Issued);
    }

    [Fact]
    public void WhenTheHandoverIsForbidden_TheListStopsBeforeIt_ButTheEarlierStepsAreStillMade()
    {
        var steps = DemoBoardTickRules.DueSteps(OrderStatus.New, Plan, Plan.ClosedAtUtc.AddHours(1), canIssue: false);

        steps.Select(s => s.To).Should().Equal(OrderStatus.Accepted, OrderStatus.Ready);
        DemoBoardTickRules.DueSteps(OrderStatus.Ready, Plan, Plan.ClosedAtUtc.AddHours(1), canIssue: false).Should().BeEmpty();
    }

    [Fact]
    public void AnOrderNotCollected_IsClosedAsNotPickedUp_FortyFiveMinutesAfterTheStart()
    {
        var notCollected = ShowcaseOrderTimeline.Plan(ShowcaseIds.For("demo", "order", "tick-npu"), Created, Pickup, OrderAcceptanceMode.Manual, ShowcaseOrderFate.NotPickedUp);

        var steps = DemoBoardTickRules.DueSteps(OrderStatus.Ready, notCollected, Pickup.AddMinutes(46), canIssue: true);

        steps.Should().ContainSingle().Which.Should().Match<ShowcaseOrderStep>(s => s.Kind == OrderEventKind.NotPickedUp && s.AtUtc == Pickup.AddMinutes(45));
        DemoBoardTickRules.DueSteps(OrderStatus.Ready, notCollected, Pickup.AddMinutes(44), canIssue: true).Should().BeEmpty();
    }

    [Fact]
    public void TheJournalNeverRunsBackwards_AStepPlannedBeforeTheLastChangeIsWrittenAtTheLastChange()
    {
        var lastChange = Pickup.AddMinutes(-20);

        DemoBoardTickRules.JournalMoment(Pickup.AddMinutes(-30), lastChange).Should().Be(lastChange);
        DemoBoardTickRules.JournalMoment(Pickup.AddMinutes(-10), lastChange).Should().Be(Pickup.AddMinutes(-10));
    }

    // ── A visitor's order (branch B) ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.New, OrderEventKind.Rejected, OrderStatus.Rejected)]
    [InlineData(OrderStatus.Accepted, OrderEventKind.CancelledByShop, OrderStatus.CancelledByShop)]
    [InlineData(OrderStatus.Ready, OrderEventKind.NotPickedUp, OrderStatus.NotPickedUp)]
    public void AnActiveOrderPastItsTimeByMoreThanFiftyMinutes_IsClosedAccordingToItsStatus(OrderStatus status, OrderEventKind kind, OrderStatus to)
    {
        var end = Pickup.AddMinutes(15);

        var closure = DemoBoardTickRules.VisitorClosure(status, Pickup, end, end.AddMinutes(51));

        closure.Should().Be(new DemoVisitorClosure(kind, to));
    }

    [Fact]
    public void ItIsNotClosedBeforeTheGraceIsOver_TheEndOfTheSlotCounts_NotTheStart()
    {
        var end = Pickup.AddMinutes(15);

        DemoBoardTickRules.VisitorClosure(OrderStatus.New, Pickup, end, end.AddMinutes(50)).Should().BeNull("exactly fifty minutes is not more than fifty");
        DemoBoardTickRules.VisitorClosure(OrderStatus.New, Pickup, end, Pickup.AddMinutes(51)).Should().BeNull("the slot is still not over plus fifty minutes");
        DemoBoardTickRules.VisitorClosure(OrderStatus.New, Pickup, end, end.AddMinutes(50).AddSeconds(1)).Should().NotBeNull();
    }

    [Fact]
    public void AnAsapOrder_CountsFromItsEstimate()
    {
        DemoBoardTickRules.VisitorClosure(OrderStatus.Accepted, Pickup, null, Pickup.AddMinutes(50)).Should().BeNull();
        DemoBoardTickRules.VisitorClosure(OrderStatus.Accepted, Pickup, null, Pickup.AddMinutes(51)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(OrderStatus.Issued)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.CancelledByCustomer)]
    [InlineData(OrderStatus.CancelledByShop)]
    [InlineData(OrderStatus.NotPickedUp)]
    public void AnOrderThatIsAlreadyOver_IsNeverClosedAgain(OrderStatus status) =>
        DemoBoardTickRules.VisitorClosure(status, Pickup, null, Pickup.AddDays(3)).Should().BeNull();

    [Fact]
    public void TheReasonTheCustomerSeesIsThePlainSentenceOfTheContract() =>
        DemoBoardTickRules.VisitorClosedReason.Should().Be("Демо: заказ закрыт автоматически — время получения прошло.");
}
