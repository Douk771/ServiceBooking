using FluentAssertions;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE23.md §396.4 — OrderMoney against the shared vectors contracts/cycle23/order-money-vectors.json.</summary>
public class OrderMoneyTests
{
    private static ProductUnit Unit(string s) => Enum.Parse<ProductUnit>(s);

    public static IEnumerable<object[]> LineVectors()
    {
        using var doc = ContractFiles.Load("cycle23", "order-money-vectors.json");
        foreach (var v in doc.RootElement.GetProperty("lines").EnumerateArray())
            yield return [v.GetProperty("unit").GetString()!, v.GetProperty("price").GetDecimal(),
                v.GetProperty("quantity").GetInt32(), v.GetProperty("expected").GetDecimal()];
    }

    [Theory]
    [MemberData(nameof(LineVectors))]
    public void LineTotal_MatchesVector(string unit, decimal price, int quantity, decimal expected) =>
        OrderMoney.LineTotal(Unit(unit), price, quantity).Should().Be(expected);

    [Fact]
    public void Totals_MatchVectors()
    {
        using var doc = ContractFiles.Load("cycle23", "order-money-vectors.json");
        foreach (var t in doc.RootElement.GetProperty("totals").EnumerateArray())
        {
            var lines = t.GetProperty("lines").EnumerateArray().ToList();
            var totals = lines.Select(l => OrderMoney.LineTotal(
                Unit(l.GetProperty("unit").GetString()!), l.GetProperty("price").GetDecimal(), l.GetProperty("quantity").GetInt32()));
            OrderMoney.Sum(totals).Should().Be(t.GetProperty("expected").GetDecimal());
            lines.Any(l => OrderMoney.IsApproximate(Unit(l.GetProperty("unit").GetString()!)))
                .Should().Be(t.GetProperty("isApproximate").GetBoolean());
        }
    }

    [Fact]
    public void WeightRule_IsRoundHalfUpToKopeck_LikeMathRoundAwayFromZero()
    {
        // The normative kopeck rule and the decimal form must agree on every value of a dense grid.
        for (var priceKop = 1; priceKop <= 3000; priceKop += 7)
        for (var grams = 10; grams <= 10_000; grams += 10)
        {
            var price = priceKop / 100m;
            var viaRound = Math.Round(price * grams / 1000m, 2, MidpointRounding.AwayFromZero);
            OrderMoney.LineTotal(ProductUnit.Weight, price, grams).Should().Be(viaRound, $"price {price}, {grams} g");
        }
    }

    [Fact]
    public void ToKopecks_IsExact_ForTwoDecimalPrices() => OrderMoney.ToKopecks(99.99m).Should().Be(9999);
}
