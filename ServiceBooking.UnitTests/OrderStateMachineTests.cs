using FluentAssertions;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE23.md §396.1, API_CONTRACT_CYCLE23.md §420.</summary>
public class OrderStateMachineTests
{
    [Fact]
    public void AvailableActions_MatchTheContractTable()
    {
        OrderStateMachine.AvailableActions(OrderStatus.New).Should().Equal(OrderAction.Accept, OrderAction.Reject, OrderAction.Edit, OrderAction.ChangePickup);
        OrderStateMachine.AvailableActions(OrderStatus.Accepted).Should().Equal(OrderAction.MarkReady, OrderAction.Cancel, OrderAction.Edit, OrderAction.ChangePickup);
        OrderStateMachine.AvailableActions(OrderStatus.Ready).Should().Equal(OrderAction.Issue, OrderAction.NotPickedUp, OrderAction.Cancel, OrderAction.Edit);
    }

    [Theory]
    [InlineData(OrderStatus.Issued)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.CancelledByCustomer)]
    [InlineData(OrderStatus.CancelledByShop)]
    [InlineData(OrderStatus.NotPickedUp)]
    public void TerminalStatuses_HaveNoActionsAndNoWayOut(OrderStatus status)
    {
        OrderStateMachine.IsTerminal(status).Should().BeTrue();
        OrderStateMachine.AvailableActions(status).Should().BeEmpty();
        foreach (var action in Enum.GetValues<OrderAction>())
            OrderStateMachine.TryApply(status, action, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.New, OrderAction.Accept, OrderStatus.Accepted)]
    [InlineData(OrderStatus.New, OrderAction.Reject, OrderStatus.Rejected)]
    [InlineData(OrderStatus.Accepted, OrderAction.MarkReady, OrderStatus.Ready)]
    [InlineData(OrderStatus.Accepted, OrderAction.Cancel, OrderStatus.CancelledByShop)]
    [InlineData(OrderStatus.Ready, OrderAction.Issue, OrderStatus.Issued)]
    [InlineData(OrderStatus.Ready, OrderAction.NotPickedUp, OrderStatus.NotPickedUp)]
    [InlineData(OrderStatus.Ready, OrderAction.Cancel, OrderStatus.CancelledByShop)]
    [InlineData(OrderStatus.New, OrderAction.Edit, OrderStatus.New)]
    [InlineData(OrderStatus.Ready, OrderAction.Edit, OrderStatus.Ready)]
    public void TryApply_ValidTransition(OrderStatus from, OrderAction action, OrderStatus expected)
    {
        OrderStateMachine.TryApply(from, action, out var to).Should().BeTrue();
        to.Should().Be(expected);
    }

    [Theory]
    [InlineData(OrderStatus.New, OrderAction.MarkReady)]
    [InlineData(OrderStatus.New, OrderAction.Issue)]
    [InlineData(OrderStatus.New, OrderAction.Cancel)]
    [InlineData(OrderStatus.Accepted, OrderAction.Accept)]
    [InlineData(OrderStatus.Accepted, OrderAction.Issue)]
    [InlineData(OrderStatus.Ready, OrderAction.Accept)]
    [InlineData(OrderStatus.Ready, OrderAction.Reject)]
    public void TryApply_InvalidTransition_IsRefused_AndKeepsStatus(OrderStatus from, OrderAction action)
    {
        OrderStateMachine.TryApply(from, action, out var to).Should().BeFalse();
        to.Should().Be(from);
    }

    [Theory]
    [InlineData(OrderStatus.New, true, true)]
    [InlineData(OrderStatus.Accepted, true, true)]
    [InlineData(OrderStatus.Ready, true, false)]
    [InlineData(OrderStatus.Issued, true, false)]
    [InlineData(OrderStatus.New, false, false)]
    [InlineData(OrderStatus.Accepted, false, false)]
    public void CanCustomerCancel_RespectsStatusAndSnapshot(OrderStatus status, bool snapshot, bool expected) =>
        OrderStateMachine.CanCustomerCancel(status, snapshot).Should().Be(expected);

    [Fact]
    public void ActiveStatuses_AreExactlyTheStockHoldingOnes() =>
        Enum.GetValues<OrderStatus>().Where(OrderStateMachine.IsActive)
            .Should().BeEquivalentTo([OrderStatus.New, OrderStatus.Accepted, OrderStatus.Ready]);
}
