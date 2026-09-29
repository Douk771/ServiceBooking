using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §449.1 / API_CONTRACT_CYCLE24.md §473.2 — validation, canonical JSON and texts of working hours.</summary>
public class ShopScheduleRulesTests
{
    private static IReadOnlyList<TimeIntervalText> Day(params (string Start, string End)[] intervals) =>
        intervals.Select(i => new TimeIntervalText(i.Start, i.End)).ToList();

    // ── time parsing ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("09:05", 545)]
    [InlineData("23:55", 1435)]
    public void TryParseTime_AcceptsHhmmOnFiveMinuteStep(string text, int minutes)
    {
        ShopScheduleRules.TryParseTime(text, out var actual, out var error).Should().BeTrue();
        actual.Should().Be(minutes);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9:00")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("ab:cd")]
    [InlineData("12-30")]
    [InlineData("12:300")]
    public void TryParseTime_WrongFormat_IsTheFormatSentence(string? text)
    {
        ShopScheduleRules.TryParseTime(text, out _, out var error).Should().BeFalse();
        error.Should().Be("Укажите время в формате ЧЧ:ММ");
    }

    [Theory]
    [InlineData("09:01")]
    [InlineData("23:58")]
    public void TryParseTime_NotAMultipleOfFive_IsTheStepSentence(string text)
    {
        ShopScheduleRules.TryParseTime(text, out _, out var error).Should().BeFalse();
        error.Should().Be("Время указывается с шагом 5 минут");
    }

    // ── one day ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseDay_OrdinaryIntervals_AreMinutes()
    {
        var r = ShopScheduleRules.ParseDay(Day(("09:00", "13:00"), ("14:00", "21:00")));
        r.Ok.Should().BeTrue();
        r.Intervals.Should().Equal(new TimeInterval(540, 780), new TimeInterval(840, 1260));
    }

    [Fact]
    public void ParseDay_EndNotAfterStart_GoesThroughMidnight()
    {
        var r = ShopScheduleRules.ParseDay(Day(("18:00", "03:00")));
        r.Intervals!.Single().Should().Be(new TimeInterval(1080, 1620));
        r.Intervals!.Single().CrossesMidnight.Should().BeTrue();
    }

    [Fact]
    public void ParseDay_EndAtMidnight_MeansUntilMidnight_NotThroughIt()
    {
        var r = ShopScheduleRules.ParseDay(Day(("22:00", "00:00")));
        r.Intervals!.Single().Should().Be(new TimeInterval(1320, 1440));
        r.Intervals!.Single().CrossesMidnight.Should().BeFalse();
    }

    [Fact]
    public void ParseDay_ZeroLength_IsRefused() =>
        ShopScheduleRules.ParseDay(Day(("10:00", "10:00"))).Error.Should().Be("Интервал не может быть нулевой длины");

    [Fact]
    public void ParseDay_MoreThanThreeIntervals_IsRefused() =>
        ShopScheduleRules.ParseDay(Day(("01:00", "02:00"), ("03:00", "04:00"), ("05:00", "06:00"), ("07:00", "08:00")))
            .Error.Should().Be("В дне не больше трёх интервалов");

    [Fact]
    public void ParseDay_Empty_IsADayOff()
    {
        var r = ShopScheduleRules.ParseDay([]);
        r.Ok.Should().BeTrue();
        r.Intervals.Should().BeEmpty();
    }

    [Theory]
    [InlineData("13:00", "14:00", "09:00", "12:00")] // out of order
    [InlineData("09:00", "13:00", "12:00", "15:00")] // overlapping
    [InlineData("09:00", "13:00", "13:00", "15:00")] // touching
    public void ParseDay_OutOfOrderOverlappingOrTouching_IsTheOrderSentence(string s1, string e1, string s2, string e2) =>
        ShopScheduleRules.ParseDay(Day((s1, e1), (s2, e2))).Error.Should().Be("Интервалы должны идти по порядку и не пересекаться");

    [Fact]
    public void ParseDay_OnlyTheLastIntervalMayCrossMidnight()
    {
        // The crossing check comes first: such an interval always overlaps the next one, so the order sentence would hide it.
        ShopScheduleRules.ParseDay(Day(("22:00", "02:00"), ("23:00", "23:30")))
            .Error.Should().Be("Через полночь может переходить только последний интервал дня");
        ShopScheduleRules.ParseDay(Day(("09:00", "12:00"), ("22:00", "02:00"))).Ok.Should().BeTrue();
    }

    // ── the week ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseWeek_DayTwice_UnknownDay()
    {
        ShopScheduleRules.ParseWeek([(DayOfWeek.Monday, Day(("09:00", "10:00"))), (DayOfWeek.Monday, Day(("11:00", "12:00")))])
            .Error.Should().Be("День недели указан дважды");
        ShopScheduleRules.ParseWeek([((DayOfWeek)99, Day(("09:00", "10:00")))]).Error.Should().Be("Неизвестный день недели");
    }

    [Fact]
    public void ParseWeek_TailAfterMidnight_MustNotReachTheNextDaysFirstInterval()
    {
        // Fri 18:00–03:00 leaves a tail until 03:00 on Saturday.
        ShopScheduleRules.ParseWeek([(DayOfWeek.Friday, Day(("18:00", "03:00"))), (DayOfWeek.Saturday, Day(("02:00", "05:00")))])
            .Error.Should().Be("Часы после полуночи пересекаются с часами следующего дня");
        ShopScheduleRules.ParseWeek([(DayOfWeek.Friday, Day(("18:00", "03:00"))), (DayOfWeek.Saturday, Day(("09:00", "12:00")))]).Ok.Should().BeTrue();
    }

    [Fact]
    public void ParseWeek_TailOfSunday_IsCheckedAgainstMonday()
    {
        ShopScheduleRules.ParseWeek([(DayOfWeek.Sunday, Day(("20:00", "04:00"))), (DayOfWeek.Monday, Day(("03:00", "10:00")))])
            .Error.Should().Be("Часы после полуночи пересекаются с часами следующего дня");
    }

    [Fact]
    public void ParseWeek_AllDaysOff_IsSetButEmpty()
    {
        var r = ShopScheduleRules.ParseWeek([]);
        r.Ok.Should().BeTrue();
        r.Hours!.Days.Should().BeEmpty();
    }

    // ── canonical JSON ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Json_RoundTrips_AndUsesMinutes()
    {
        var week = ShopScheduleRules.ParseWeek([(DayOfWeek.Monday, Day(("09:00", "13:00"), ("14:00", "21:00"))), (DayOfWeek.Friday, Day(("18:00", "03:00")))]).Hours!;
        var json = ShopScheduleRules.Serialize(week);
        json.Should().Contain("\"Monday\"").And.Contain("\"start\":540").And.Contain("\"end\":1620");
        var back = ShopScheduleRules.Parse(json)!;
        back.For(DayOfWeek.Monday).Should().Equal(new TimeInterval(540, 780), new TimeInterval(840, 1260));
        back.For(DayOfWeek.Friday).Should().Equal(new TimeInterval(1080, 1620));
        back.For(DayOfWeek.Sunday).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"nodays\":1}")]
    public void Parse_MissingOrBrokenJson_IsHoursNotSet_NeverOpenAllDay(string? json) =>
        ShopScheduleRules.Parse(json).Should().BeNull();

    [Fact]
    public void DayText_Variants()
    {
        ShopScheduleRules.DayText([]).Should().Be("выходной");
        ShopScheduleRules.DayText([new TimeInterval(540, 840), new TimeInterval(900, 1260)]).Should().Be("09:00–14:00, 15:00–21:00");
        ShopScheduleRules.DayText([new TimeInterval(1080, 1620)]).Should().Be("18:00–03:00 (до утра)");
        ShopScheduleRules.DayText([new TimeInterval(1320, 1440)]).Should().Be("22:00–00:00");
    }
}
