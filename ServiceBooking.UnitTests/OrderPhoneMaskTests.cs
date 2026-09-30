using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;

namespace ServiceBooking.UnitTests;

public class OrderPhoneMaskTests
{
    [Theory]
    [InlineData("79991231234", "+7 (···) ···-12-34")]
    [InlineData("79001234567", "+7 (···) ···-45-67")]
    [InlineData("375291234567", "···4567")]
    [InlineData("1234", "····")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Mask_ShowsOnlyTheLastFourDigits(string? phone, string expected) => OrderPhoneMask.Mask(phone).Should().Be(expected);

    [Fact]
    public void Mask_NeverLeaksTheOperatorCode() => OrderPhoneMask.Mask("79991234567").Should().NotContain("999");
}
