using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;

namespace ServiceBooking.UnitTests;

public class ShopSummaryMathTests
{
    [Fact]
    public void Share_IsAWholePercent_AndDashWithoutBase()
    {
        ShopSummaryMath.Share(3, 37).Text.Should().Be("8 %");
        ShopSummaryMath.Share(1, 37).Text.Should().Be("3 %");
        ShopSummaryMath.Share(1, 8).Text.Should().Be("13 %");        // 12.5 rounds away from zero
        ShopSummaryMath.Share(0, 0).Should().Be((null, "—"));
    }

    [Fact]
    public void AverageCheck_RoundsKopecks_AndIsNullWithoutIssued()
    {
        ShopSummaryMath.AverageCheck(16250m, 30).Should().Be(541.67m);
        ShopSummaryMath.AverageCheck(100m, 3).Should().Be(33.33m);
        ShopSummaryMath.AverageCheck(0.05m, 2).Should().Be(0.03m);   // 2.5 kopecks → 3
        ShopSummaryMath.AverageCheck(0m, 0).Should().BeNull();
        ShopSummaryMath.AverageCheckText(541.67m).Should().Be("541,67 ₽");
        ShopSummaryMath.AverageCheckText(null).Should().Be("—");
    }

    [Theory]
    [InlineData(112, 100, "+12 %")]
    [InlineData(97, 100, "−3 %")]
    [InlineData(100, 100, "0 %")]
    [InlineData(50, 0, "—")]
    [InlineData(0, 0, "—")]
    [InlineData(0, 10, "−100 %")]
    public void Delta(int current, int previous, string expected) => ShopSummaryMath.Delta(current, previous).Should().Be(expected);

    [Fact]
    public void DeltaOfMoney_UsesTheSameRule() => ShopSummaryMath.Delta(1123.5m, 1000m).Should().Be("+12 %");
}
