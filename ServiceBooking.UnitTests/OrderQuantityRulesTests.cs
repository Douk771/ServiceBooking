using FluentAssertions;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE23.md §395.2 step 8, §396.4, API_CONTRACT_CYCLE23.md §410.2.</summary>
public class OrderQuantityRulesTests
{
    [Theory]
    [InlineData(1, QuantityCheck.Ok)]
    [InlineData(99, QuantityCheck.Ok)]
    [InlineData(0, QuantityCheck.InvalidQuantity)]
    [InlineData(-1, QuantityCheck.InvalidQuantity)]
    [InlineData(100, QuantityCheck.InvalidQuantity)]
    public void Piece(int quantity, QuantityCheck expected) =>
        OrderQuantityRules.Check(ProductUnit.Piece, quantity, null, null).Should().Be(expected);

    [Theory]
    [InlineData(100, 100, 300, QuantityCheck.BelowMinimum)]   // multiple of step but below the minimum
    [InlineData(300, 100, 300, QuantityCheck.Ok)]
    [InlineData(350, 100, 300, QuantityCheck.InvalidQuantity)] // off the step
    [InlineData(10000, 100, 300, QuantityCheck.Ok)]
    [InlineData(10100, 100, 300, QuantityCheck.InvalidQuantity)]
    [InlineData(50, 50, 50, QuantityCheck.Ok)]
    public void Weight(int grams, int step, int min, QuantityCheck expected) =>
        OrderQuantityRules.Check(ProductUnit.Weight, grams, step, min).Should().Be(expected);

    [Fact]
    public void Weight_NoMinimum_DefaultsToStep()
    {
        OrderQuantityRules.MinQuantity(ProductUnit.Weight, 250, null).Should().Be(250);
        OrderQuantityRules.MinQuantity(ProductUnit.Weight, null, null).Should().Be(100);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(99, true)]
    [InlineData(100, false)]
    [InlineData(0, false)]
    public void Edit_Piece(int quantity, bool expected) =>
        OrderQuantityRules.IsValidForEdit(ProductUnit.Piece, quantity, null).Should().Be(expected);

    [Theory]
    [InlineData(200, 100, true)]
    [InlineData(150, 100, false)]
    [InlineData(10001, 1, false)]
    public void Edit_Weight_UsesTheLineStep(int grams, int step, bool expected) =>
        OrderQuantityRules.IsValidForEdit(ProductUnit.Weight, grams, step).Should().Be(expected);

    [Theory]
    [InlineData(1, true)]
    [InlineData(537, true)]
    [InlineData(100_000, true)]
    [InlineData(0, false)]
    [InlineData(100_001, false)]
    public void ActualWeight_AnyValueInRange(int grams, bool expected) =>
        OrderQuantityRules.IsValidActualWeight(grams).Should().Be(expected);

    [Theory]
    [InlineData(null, null, true, 100, 100)]
    [InlineData(50, null, true, 50, 50)]
    [InlineData(100, 300, true, 100, 300)]
    [InlineData(9, null, false, 0, 0)]
    [InlineData(5001, null, false, 0, 0)]
    [InlineData(100, 150, false, 0, 0)]   // not a multiple of the step
    [InlineData(200, 100, false, 0, 0)]   // below the step
    [InlineData(100, 10100, false, 0, 0)] // above 10 000
    public void NormalizeWeight(int? step, int? min, bool ok, int expectedStep, int expectedMin)
    {
        OrderQuantityRules.TryNormalizeWeight(step, min, out var s, out var m, out var error).Should().Be(ok);
        if (ok)
        {
            s.Should().Be(expectedStep);
            m.Should().Be(expectedMin);
            error.Should().BeNull();
        }
        else error.Should().NotBeNullOrEmpty();
    }
}
