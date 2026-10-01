using FluentAssertions;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE35.md §35.10.2 (A35-5) — the live board of the demo as two pure functions: the offsets of a planned order and its state at a moment.</summary>
public class ShowcaseOrderTimelineTests
{
    private static readonly DateTime Pickup = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);

    private static Guid Id(int n) => ShowcaseIds.For("demo", "order", $"timeline-test:{n}");

    [Fact]
    public void Plan_IsDeterministic_ForTheSameOrder()
    {
        var created = Pickup.AddMinutes(-40);

        var a = ShowcaseOrderTimeline.Plan(Id(1), created, Pickup, OrderAcceptanceMode.Manual);
        var b = ShowcaseOrderTimeline.Plan(Id(1), created, Pickup, OrderAcceptanceMode.Manual);

        a.Should().Be(b);
        ShowcaseOrderTimeline.Plan(Id(2), created, Pickup, OrderAcceptanceMode.Manual).Should().NotBe(a, "the offsets come from the order id");
    }

    [Fact]
    public void Plan_OfAnOrderPlacedShortlyBeforeTheSlot_AcceptsThirtyToFiftyMinutesBefore_ReadyFiveToFifteen_HandsOverThreeToTwentyAfter()
    {
        var created = Pickup.AddHours(-6);
        for (var n = 0; n < 200; n++)
        {
            var plan = ShowcaseOrderTimeline.Plan(Id(n), created, Pickup, OrderAcceptanceMode.Manual, ShowcaseOrderFate.Issued);

            (Pickup - plan.AcceptedAtUtc).TotalMinutes.Should().BeInRange(30, 50);
            (Pickup - plan.ReadyAtUtc).TotalMinutes.Should().BeInRange(5, 15);
            (plan.ClosedAtUtc - Pickup).TotalMinutes.Should().BeInRange(3, 20);
        }
    }

    [Fact]
    public void Plan_OfAnOrderPlacedMoreThanTwelveHoursAhead_IsAcceptedSoonAfterCreation_ButAnHourBeforeThePickupAtTheLatest()
    {
        var created = Pickup.AddHours(-20);
        for (var n = 0; n < 200; n++)
        {
            var plan = ShowcaseOrderTimeline.Plan(Id(n), created, Pickup, OrderAcceptanceMode.Manual);

            (plan.AcceptedAtUtc - created).TotalMinutes.Should().BeInRange(1, 240);
            plan.AcceptedAtUtc.Should().BeOnOrBefore(Pickup.AddMinutes(-60));
        }
    }

    [Fact]
    public void Plan_OfAnAsapOrder_NeverAcceptsBeforeItWasCreated_AndEveryMomentIsAfterThePreviousOne()
    {
        // "as soon as possible": created 15 minutes before the estimate; 30–50 minutes before the pickup would be before creation.
        var created = Pickup.AddMinutes(-15);
        for (var n = 0; n < 300; n++)
        {
            var plan = ShowcaseOrderTimeline.Plan(Id(n), created, Pickup, OrderAcceptanceMode.Manual);

            plan.AcceptedAtUtc.Should().BeAfter(created);
            plan.ReadyAtUtc.Should().BeAfter(plan.AcceptedAtUtc);
            plan.ClosedAtUtc.Should().BeAfter(plan.ReadyAtUtc);
        }
    }

    [Fact]
    public void Plan_InTheAutomaticMode_IsBornAccepted_AndHasNoAcceptStep()
    {
        var created = Pickup.AddHours(-3);

        var plan = ShowcaseOrderTimeline.Plan(Id(1), created, Pickup, OrderAcceptanceMode.Auto);

        plan.InitialStatus.Should().Be(OrderStatus.Accepted);
        plan.AcceptedAtUtc.Should().Be(created);
        ShowcaseOrderTimeline.AllSteps(plan).Select(s => s.Kind).Should().NotContain(OrderEventKind.Accepted);
        ShowcaseOrderTimeline.AllSteps(plan)[0].From.Should().Be(OrderStatus.Accepted);
    }

    [Fact]
    public void Plan_AboutFivePercentAreNotCollected_ClosedFortyFiveMinutesAfterTheStart()
    {
        var created = Pickup.AddHours(-3);
        var plans = Enumerable.Range(0, 4000).Select(n => ShowcaseOrderTimeline.Plan(Id(n), created, Pickup, OrderAcceptanceMode.Manual)).ToList();

        var notPickedUp = plans.Where(p => p.Fate == ShowcaseOrderFate.NotPickedUp).ToList();

        (notPickedUp.Count / 4000.0).Should().BeInRange(0.035, 0.065);
        notPickedUp.Should().OnlyContain(p => p.ClosedAtUtc == Pickup.AddMinutes(ShowcaseOrderTimeline.NotPickedUpAfterMinutes));
        notPickedUp.Should().OnlyContain(p => p.FinalStatus == OrderStatus.NotPickedUp);
    }

    [Fact]
    public void Plan_TheGeneratorMayForceTheFate_ButTheOffsetsStayTheSame()
    {
        var created = Pickup.AddHours(-3);
        var free = ShowcaseOrderTimeline.Plan(Id(7), created, Pickup, OrderAcceptanceMode.Manual, ShowcaseOrderFate.Issued);
        var forced = ShowcaseOrderTimeline.Plan(Id(7), created, Pickup, OrderAcceptanceMode.Manual, ShowcaseOrderFate.NotPickedUp);

        forced.Fate.Should().Be(ShowcaseOrderFate.NotPickedUp);
        forced.AcceptedAtUtc.Should().Be(free.AcceptedAtUtc);
        forced.ReadyAtUtc.Should().Be(free.ReadyAtUtc);
    }

    [Fact]
    public void StateAt_WalksTheStatusesInOrder_AndReturnsOnlyTheStepsThatAlreadyHappened()
    {
        var created = Pickup.AddHours(-3);
        var plan = ShowcaseOrderTimeline.Plan(Id(3), created, Pickup, OrderAcceptanceMode.Manual, ShowcaseOrderFate.Issued);

        ShowcaseOrderTimeline.StateAt(plan, created).Status.Should().Be(OrderStatus.New);
        ShowcaseOrderTimeline.StateAt(plan, created).Steps.Should().BeEmpty();

        var afterAccept = ShowcaseOrderTimeline.StateAt(plan, plan.AcceptedAtUtc);
        afterAccept.Status.Should().Be(OrderStatus.Accepted);
        afterAccept.Steps.Should().ContainSingle().Which.Kind.Should().Be(OrderEventKind.Accepted);

        ShowcaseOrderTimeline.StateAt(plan, plan.ReadyAtUtc).Status.Should().Be(OrderStatus.Ready);

        var done = ShowcaseOrderTimeline.StateAt(plan, plan.ClosedAtUtc.AddHours(5));
        done.Status.Should().Be(OrderStatus.Issued);
        done.Steps.Select(s => s.To).Should().Equal(OrderStatus.Accepted, OrderStatus.Ready, OrderStatus.Issued);
    }

    [Fact]
    public void StateAt_EveryStepStartsWhereThePreviousOneEnded_NoStatusIsSkipped()
    {
        var created = Pickup.AddHours(-3);
        var plan = ShowcaseOrderTimeline.Plan(Id(11), created, Pickup, OrderAcceptanceMode.Manual);

        var steps = ShowcaseOrderTimeline.AllSteps(plan);

        steps[0].From.Should().Be(OrderStatus.New);
        for (var i = 1; i < steps.Count; i++)
        {
            steps[i].From.Should().Be(steps[i - 1].To);
            steps[i].AtUtc.Should().BeAfter(steps[i - 1].AtUtc);
        }
        steps[^1].To.Should().Be(plan.FinalStatus);
    }

    [Fact]
    public void StateAt_TheStepIsThereFromItsOwnMoment_NotABeforeIt()
    {
        var plan = ShowcaseOrderTimeline.Plan(Id(5), Pickup.AddHours(-3), Pickup, OrderAcceptanceMode.Manual);

        ShowcaseOrderTimeline.StateAt(plan, plan.ReadyAtUtc.AddTicks(-1)).Status.Should().Be(OrderStatus.Accepted);
        ShowcaseOrderTimeline.StateAt(plan, plan.ReadyAtUtc).Status.Should().Be(OrderStatus.Ready);
    }
}
