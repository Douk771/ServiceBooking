using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;

namespace ServiceBooking.UnitTests;

public class ReportTruncationTests
{
    [Theory]
    [InlineData(98)]
    [InlineData(99)]
    [InlineData(100)]
    public void Truncate_never_leaves_lone_surrogate(int prefix)
    {
        var text = new string('a', prefix) + "🌸zz";
        var cut = ShopReportService.TruncateWithoutSplittingPair(text, 100)!;
        cut.Length.Should().BeLessThanOrEqualTo(100);
        cut.Should().NotBeEmpty();
        foreach (var (c, i) in cut.Select((c, i) => (c, i)))
            if (char.IsHighSurrogate(c)) (i + 1 < cut.Length && char.IsLowSurrogate(cut[i + 1])).Should().BeTrue();
    }

    [Fact]
    public void Truncate_keeps_short_and_null() =>
        (ShopReportService.TruncateWithoutSplittingPair(null, 100), ShopReportService.TruncateWithoutSplittingPair("abc", 100)).Should().Be((null, "abc"));
}
