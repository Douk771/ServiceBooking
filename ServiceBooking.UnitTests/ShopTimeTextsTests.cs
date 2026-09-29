using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE24.md §470 — "H:mm" in sentences, "HH:mm" in fields; Russian short names.</summary>
public class ShopTimeTextsTests
{
    [Theory]
    [InlineData(0, "0:00", "00:00")]
    [InlineData(65, "1:05", "01:05")]
    [InlineData(540, "9:00", "09:00")]
    [InlineData(1260, "21:00", "21:00")]
    [InlineData(1440, "0:00", "00:00")]
    [InlineData(1620, "3:00", "03:00")]
    public void Clock_NoLeadingZeroInSentences_LeadingZeroInFields(int minutes, string sentence, string field)
    {
        ShopTimeTexts.Clock(minutes).Should().Be(sentence);
        ShopTimeTexts.Hhmm(minutes).Should().Be(field);
    }

    [Fact]
    public void DateLabels()
    {
        var today = new DateOnly(2026, 9, 30);
        ShopTimeTexts.DateLabel(today, today).Should().Be("Сегодня");
        ShopTimeTexts.DateLabel(today.AddDays(1), today).Should().Be("Завтра");
        ShopTimeTexts.DateLabel(new DateOnly(2026, 10, 2), today).Should().Be("пт 2 окт");
        ShopTimeTexts.DayName(DayOfWeek.Sunday).Should().Be("вс");
        ShopTimeTexts.DayName(DayOfWeek.Monday).Should().Be("пн");
        ShopTimeTexts.DayMonth(new DateOnly(2026, 5, 9)).Should().Be("9 мая");
    }

    [Fact]
    public void MonthTexts()
    {
        ShopTimeTexts.MonthLabel(new DateOnly(2026, 10, 14)).Should().Be("октябрь 2026");
        ShopTimeTexts.MonthName(new DateOnly(2026, 3, 1)).Should().Be("март");
        ShopTimeTexts.FirstOfNextMonth(new DateOnly(2026, 9, 30)).Should().Be("1 октября");
        ShopTimeTexts.FirstOfNextMonth(new DateOnly(2026, 12, 5)).Should().Be("1 января");
    }

    [Theory]
    [InlineData(1, "позиция")]
    [InlineData(2, "позиции")]
    [InlineData(4, "позиции")]
    [InlineData(5, "позиций")]
    [InlineData(11, "позиций")]
    [InlineData(12, "позиций")]
    [InlineData(21, "позиция")]
    [InlineData(101, "позиция")]
    [InlineData(111, "позиций")]
    public void Plural(int n, string expected) => ShopTimeTexts.Plural(n, "позиция", "позиции", "позиций").Should().Be(expected);
}
