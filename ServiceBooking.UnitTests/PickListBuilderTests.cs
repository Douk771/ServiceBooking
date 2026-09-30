using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §503 — grouping of the pick list (pure).</summary>
public class PickListBuilderTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly DateTime Noon = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Borsch = Guid.NewGuid();
    private static readonly Guid Cheese = Guid.NewGuid();

    private static IReadOnlyList<PickupSlot> Slots() =>
    [
        new(Noon, Noon.AddMinutes(15), "12:00–12:15"),
        new(Noon.AddMinutes(15), Noon.AddMinutes(30), "12:15–12:30")
    ];

    private static PickListItemInput Item(Guid id, string name, ProductUnit unit, int qty, int cat = 0, int pos = 0, string? category = "Супы") =>
        new(id, name, unit, qty, null, category, cat, pos);

    private static PickListOrderInput Order(int number, OrderStatus status, DateTime start, PickupKind kind = PickupKind.Slot, params PickListItemInput[] items) =>
        new(Guid.NewGuid(), number, status, kind, start, start, number == 1 ? "без лука" : null, items);

    [Fact]
    public void ByProduct_SumsPiecesAndOrders_AndMarksUnaccepted()
    {
        var result = PickListBuilder.Build(
        [
            Order(1, OrderStatus.Accepted, Noon, PickupKind.Slot, Item(Borsch, "Борщ", ProductUnit.Piece, 2)),
            Order(2, OrderStatus.New, Noon.AddMinutes(5), PickupKind.Slot, Item(Borsch, "Борщ", ProductUnit.Piece, 3))
        ], Slots(), Zone);

        var row = result.ByProduct.Single();
        (row.TotalQuantity, row.QuantityText, row.OrderCount, row.HasUnaccepted).Should().Be((5, "5 шт", 2, true));
        row.WeightBreakdownText.Should().BeNull();
    }

    [Fact]
    public void ByProduct_Weight_HasBreakdownPerOrder()
    {
        var result = PickListBuilder.Build(
        [
            Order(1, OrderStatus.Accepted, Noon, PickupKind.Slot, Item(Cheese, "Сыр", ProductUnit.Weight, 500)),
            Order(2, OrderStatus.Accepted, Noon.AddMinutes(1), PickupKind.Slot, Item(Cheese, "Сыр", ProductUnit.Weight, 1200)),
            Order(3, OrderStatus.Accepted, Noon.AddMinutes(2), PickupKind.Slot, Item(Cheese, "Сыр", ProductUnit.Weight, 650))
        ], Slots(), Zone);

        var row = result.ByProduct.Single();
        row.QuantityText.Should().Be("2,35 кг");
        row.WeightBreakdownText.Should().Be("3 заказа: 500 г, 1,2 кг, 650 г");
        row.HasUnaccepted.Should().BeFalse();
    }

    [Fact]
    public void ByProduct_FollowsCatalogOrder_DeletedAtTheEndByName()
    {
        var a = Guid.NewGuid();
        var result = PickListBuilder.Build(
        [
            Order(1, OrderStatus.Accepted, Noon, PickupKind.Slot,
                new PickListItemInput(null, "Яблоко удалённое", ProductUnit.Piece, 1, null, null, int.MaxValue, int.MaxValue),
                new PickListItemInput(null, "Арбуз удалённый", ProductUnit.Piece, 1, null, null, int.MaxValue, int.MaxValue),
                Item(Cheese, "Сыр", ProductUnit.Weight, 300, cat: 1, pos: 0, category: "Гастрономия"),
                Item(a, "Борщ", ProductUnit.Piece, 1, cat: 0, pos: 5))
        ], Slots(), Zone);

        result.ByProduct.Select(r => r.Name).Should().Equal("Борщ", "Сыр", "Арбуз удалённый", "Яблоко удалённое");
    }

    [Fact]
    public void ByProduct_SameProductInPiecesAndWeight_AreTwoRows() =>
        PickListBuilder.Build([Order(1, OrderStatus.Accepted, Noon, PickupKind.Slot,
            Item(Cheese, "Сыр", ProductUnit.Piece, 1), Item(Cheese, "Сыр", ProductUnit.Weight, 300))], Slots(), Zone).ByProduct.Should().HaveCount(2);

    [Fact]
    public void ByTime_GroupsBySlot_AsapByTheirLandmark_AndOutOfGridLast()
    {
        var result = PickListBuilder.Build(
        [
            Order(1, OrderStatus.Accepted, Noon, PickupKind.Slot, Item(Borsch, "Борщ", ProductUnit.Piece, 1)),
            Order(2, OrderStatus.New, Noon.AddMinutes(20), PickupKind.Asap, Item(Borsch, "Борщ", ProductUnit.Piece, 1)),
            Order(3, OrderStatus.Accepted, Noon.AddHours(5), PickupKind.Slot, Item(Borsch, "Борщ", ProductUnit.Piece, 1))
        ], Slots(), Zone);

        result.ByTime.Select(g => g.Label).Should().Equal("12:00–12:15", "12:15–12:30", PickListBuilder.OutOfScheduleLabel);
        result.ByTime[0].From.Should().Be("12:00");
        result.ByTime[0].To.Should().Be("12:15");
        result.ByTime[2].From.Should().BeNull();
        result.ByTime[0].Orders.Single().PickupText.Should().Be("к 12:00");
        result.ByTime[0].Orders.Single().Comment.Should().Be("без лука");
        var asap = result.ByTime[1].Orders.Single();
        (asap.PickupText, asap.IsUnaccepted).Should().Be(("≈ 12:20", true));
    }

    [Fact]
    public void Output_HasNoCustomerData() =>
        typeof(PickListOrderRow).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Phone") || n.Contains("Customer") || n == "Name");
}
