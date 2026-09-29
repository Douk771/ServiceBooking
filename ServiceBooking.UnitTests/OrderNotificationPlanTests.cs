using FluentAssertions;
using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §458 — the table "journal event → who is told what", in full.</summary>
public class OrderNotificationPlanTests
{
    private static readonly OrderNotificationFlags AllOn = new(StaffPushEnabled: true, CustomerWebPushEnabled: true, CustomerMessengerEnabled: true, OrderNotifyByMessenger: true);

    private static OrderNotificationPlan For(OrderEventKind kind, OrderStatus? to = null, OrderNotificationFlags? flags = null) =>
        OrderNotificationPlan.For(kind, to, flags ?? AllOn);

    [Theory]
    [InlineData(OrderEventKind.Accepted, NotificationType.OrderAccepted)]
    [InlineData(OrderEventKind.MarkedReady, NotificationType.OrderReady)]
    [InlineData(OrderEventKind.Rejected, NotificationType.OrderRejected)]
    [InlineData(OrderEventKind.CancelledByShop, NotificationType.OrderCancelledByShop)]
    [InlineData(OrderEventKind.Edited, NotificationType.OrderEditedByShop)]
    [InlineData(OrderEventKind.PickupChanged, NotificationType.OrderPickupChanged)]
    public void ShopActions_TellTheCustomer_OnBothChannels_AndNotStaff(OrderEventKind kind, NotificationType type)
    {
        var plan = For(kind);
        plan.CustomerType.Should().Be(type);
        (plan.CustomerWebPush, plan.CustomerMessenger).Should().Be((true, true));
        plan.StaffType.Should().BeNull();
    }

    [Theory]
    [InlineData(OrderEventKind.Issued)]
    [InlineData(OrderEventKind.NotPickedUp)]
    public void IssuedAndNotPickedUp_TellNobody(OrderEventKind kind) => For(kind).IsEmpty.Should().BeTrue();

    [Fact]
    public void Created_TellsStaff_AndTheCustomerOnlyOnAutoAccept()
    {
        var manual = For(OrderEventKind.Created, OrderStatus.New);
        manual.StaffType.Should().Be(NotificationType.StaffOrderCreated);
        manual.CustomerType.Should().BeNull();

        var auto = For(OrderEventKind.Created, OrderStatus.Accepted);
        auto.StaffType.Should().Be(NotificationType.StaffOrderCreated);
        auto.CustomerType.Should().Be(NotificationType.OrderAccepted);
    }

    [Fact]
    public void CancelledByCustomer_TellsStaff_NeverTheCustomerWhoDidIt()
    {
        var plan = For(OrderEventKind.CancelledByCustomer);
        plan.StaffType.Should().Be(NotificationType.StaffOrderCancelledByCustomer);
        plan.CustomerType.Should().BeNull();
    }

    [Fact]
    public void StaffFlag_SwitchesStaffOff_NotTheCustomer()
    {
        var plan = For(OrderEventKind.Created, OrderStatus.Accepted, AllOn with { StaffPushEnabled = false });
        plan.StaffType.Should().BeNull();
        plan.CustomerType.Should().Be(NotificationType.OrderAccepted);
    }

    [Fact]
    public void ChannelSwitches_AreIndependent()
    {
        var webOnly = For(OrderEventKind.MarkedReady, null, AllOn with { CustomerMessengerEnabled = false });
        (webOnly.CustomerWebPush, webOnly.CustomerMessenger).Should().Be((true, false));

        // The messenger needs BOTH the shop's flag and the customer's own choice on the order.
        var noChoice = For(OrderEventKind.MarkedReady, null, AllOn with { OrderNotifyByMessenger = false });
        (noChoice.CustomerWebPush, noChoice.CustomerMessenger).Should().Be((true, false));

        var messengerOnly = For(OrderEventKind.MarkedReady, null, AllOn with { CustomerWebPushEnabled = false });
        (messengerOnly.CustomerWebPush, messengerOnly.CustomerMessenger).Should().Be((false, true));
    }

    [Fact]
    public void NoCustomerChannel_MeansNoCustomerNotificationAtAll()
    {
        var plan = For(OrderEventKind.Accepted, null, AllOn with { CustomerWebPushEnabled = false, OrderNotifyByMessenger = false });
        plan.CustomerType.Should().BeNull();
        plan.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void EveryJournalKind_IsCovered_ByTheTable()
    {
        // A new OrderEventKind must be a conscious decision here: this fails when one is added without touching the planner.
        var told = new[]
        {
            OrderEventKind.Created, OrderEventKind.Accepted, OrderEventKind.Rejected, OrderEventKind.MarkedReady, OrderEventKind.CancelledByCustomer,
            OrderEventKind.CancelledByShop, OrderEventKind.Edited, OrderEventKind.PickupChanged
        };
        var silent = new[] { OrderEventKind.Issued, OrderEventKind.NotPickedUp };
        Enum.GetValues<OrderEventKind>().Should().BeEquivalentTo(told.Concat(silent));
        foreach (var kind in told) For(kind, OrderStatus.Accepted).IsEmpty.Should().BeFalse(kind.ToString());
        foreach (var kind in silent) For(kind).IsEmpty.Should().BeTrue(kind.ToString());
    }
}
