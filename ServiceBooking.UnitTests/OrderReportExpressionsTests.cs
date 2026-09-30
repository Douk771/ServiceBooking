using FluentAssertions;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Orders.Reports;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §501.2, R-5 — the report total is the order card's total for every status.</summary>
public class OrderReportExpressionsTests
{
    [Theory]
    [InlineData(OrderStatus.New, true)]
    [InlineData(OrderStatus.Accepted, true)]
    [InlineData(OrderStatus.Ready, true)]
    [InlineData(OrderStatus.Issued, true)]
    [InlineData(OrderStatus.Issued, false)]
    [InlineData(OrderStatus.Rejected, true)]
    [InlineData(OrderStatus.CancelledByCustomer, true)]
    [InlineData(OrderStatus.CancelledByShop, true)]
    [InlineData(OrderStatus.NotPickedUp, true)]
    public void Total_EqualsDisplayTotal(OrderStatus status, bool hasFinal)
    {
        var order = new Order { Status = status, EstimatedTotal = 500m, FinalTotal = hasFinal ? 480.5m : null };
        OrderReportExpressions.TotalOf(order).Should().Be(OrderDtoMapper.DisplayTotal(order));
    }

    [Fact]
    public void Total_OfIssued_IsTheActualWeightTotal() =>
        OrderReportExpressions.TotalOf(new Order { Status = OrderStatus.Issued, EstimatedTotal = 500m, FinalTotal = 480.5m }).Should().Be(480.5m);

    [Fact]
    public void Total_OfNotIssued_IgnoresFinal() =>
        OrderReportExpressions.TotalOf(new Order { Status = OrderStatus.Ready, EstimatedTotal = 500m, FinalTotal = 480.5m }).Should().Be(500m);
}
