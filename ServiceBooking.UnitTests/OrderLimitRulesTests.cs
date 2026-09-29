using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §459.6 — the 80 % / 100 % thresholds and the limit texts.</summary>
public class OrderLimitRulesTests
{
    [Theory]
    [InlineData(150, 120)]
    [InlineData(100, 80)]
    [InlineData(15, 12)]   // floating point would give 12.000000000000002 → 13
    [InlineData(10, 8)]
    [InlineData(1, 1)]
    [InlineData(7, 6)]     // ceil(5.6)
    public void Warning80Threshold_IsCeilOfEightyPercent_InIntegerArithmetic(int limit, int expected) =>
        OrderLimitRules.Warning80Threshold(limit).Should().Be(expected);

    [Theory]
    [InlineData(0, 150, OrderLimitWarningLevel.None)]
    [InlineData(119, 150, OrderLimitWarningLevel.None)]
    [InlineData(120, 150, OrderLimitWarningLevel.Warning80)]
    [InlineData(149, 150, OrderLimitWarningLevel.Warning80)]
    [InlineData(150, 150, OrderLimitWarningLevel.Reached)]
    [InlineData(151, 150, OrderLimitWarningLevel.Reached)]
    public void Level_ByUsage(int used, int limit, OrderLimitWarningLevel level) => OrderLimitRules.Level(used, limit).Should().Be(level);

    [Fact]
    public void Level_NoLimit_IsNone() => OrderLimitRules.Level(1_000_000, null).Should().Be(OrderLimitWarningLevel.None);

    [Fact]
    public void Describe_CarriesTheMonthAndTheText()
    {
        OrderLimitRules.Describe(120, 150, new DateOnly(2026, 10, 14)).Should()
            .Be(new OrderLimitInfo(120, 150, "октябрь 2026", OrderLimitWarningLevel.Warning80, "Заказов в этом месяце: 120 из 150"));
        var unlimited = OrderLimitRules.Describe(7, null, new DateOnly(2026, 10, 14));
        (unlimited.Limit, unlimited.Text, unlimited.WarningLevel).Should().Be((null, null, OrderLimitWarningLevel.None));
    }

    [Fact]
    public void ReachedOwnerText_NamesTheNextMonth()
    {
        OrderLimitRules.ReachedOwnerText(150, 150, new DateOnly(2026, 10, 3)).Should()
            .Be("Лимит заказов на октябрь исчерпан (150 из 150). Новые заказы примутся с 1 ноября или после смены тарифа");
        OrderLimitRules.ReachedOwnerText(10, 10, new DateOnly(2026, 12, 31)).Should().Contain("с 1 января");
    }
}
