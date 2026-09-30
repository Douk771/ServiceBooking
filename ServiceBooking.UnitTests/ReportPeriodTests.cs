using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE25.md §522 — periods and labels, computed only on the server.</summary>
public class ReportPeriodTests
{
    private static readonly DateOnly Wd = new(2026, 9, 30);

    private static ReportPeriod Resolve(ReportPeriodPreset p, DateOnly? from = null, DateOnly? to = null)
    {
        ReportPeriod.TryResolve(p, from, to, Wd, out var period, out var error).Should().BeTrue(error);
        return period;
    }

    [Theory]
    [InlineData(ReportPeriodPreset.Today, "2026-09-30", "2026-09-30", "Сегодня, 30 сен", 1)]
    [InlineData(ReportPeriodPreset.Yesterday, "2026-09-29", "2026-09-29", "Вчера, 29 сен", 1)]
    [InlineData(ReportPeriodPreset.Last7Days, "2026-09-24", "2026-09-30", "7 дней: 24–30 сен", 7)]
    [InlineData(ReportPeriodPreset.Last30Days, "2026-09-01", "2026-09-30", "30 дней: 1–30 сен", 30)]
    [InlineData(ReportPeriodPreset.ThisMonth, "2026-09-01", "2026-09-30", "Сентябрь 2026", 30)]
    [InlineData(ReportPeriodPreset.LastMonth, "2026-08-01", "2026-08-31", "Август 2026", 31)]
    public void Presets_ResolveFromTheWorkingDay(ReportPeriodPreset preset, string from, string to, string label, int days)
    {
        var p = Resolve(preset);
        p.From.Should().Be(DateOnly.Parse(from));
        p.To.Should().Be(DateOnly.Parse(to));
        p.Label.Should().Be(label);
        p.Days.Should().Be(days);
    }

    [Fact]
    public void Range_AcrossMonths_UsesBothMonths()
    {
        ReportPeriod.TryResolve(ReportPeriodPreset.Last7Days, null, null, new DateOnly(2026, 10, 3), out var p, out _).Should().BeTrue();
        p.Label.Should().Be("7 дней: 27 сен – 3 окт");
    }

    [Fact]
    public void Working_DayAfterMidnight_IsTheStartDay() =>
        // The caller passes the WORKING day (Friday) while the calendar says Saturday: the period follows the working day.
        ReportPeriod.TryResolve(ReportPeriodPreset.Today, null, null, new DateOnly(2026, 10, 2), out var p, out _).Should().BeTrue();

    [Fact]
    public void Custom_LabelAndBounds()
    {
        Resolve(ReportPeriodPreset.Custom, new DateOnly(2026, 8, 15), Wd).Label.Should().Be("15 авг – 30 сен 2026");
        Resolve(ReportPeriodPreset.Custom, Wd, Wd).Label.Should().Be("30 сен 2026");
        Resolve(ReportPeriodPreset.Custom, new DateOnly(2025, 12, 30), Wd).Label.Should().Be("30 дек 2025 – 30 сен 2026");
        Resolve(ReportPeriodPreset.Custom, new DateOnly(2025, 9, 30), Wd).Days.Should().Be(366);
    }

    [Fact]
    public void Custom_Errors()
    {
        ReportPeriod.TryResolve(ReportPeriodPreset.Custom, null, Wd, Wd, out _, out var e1).Should().BeFalse();
        e1.Should().Be("Укажите начало и конец периода");
        ReportPeriod.TryResolve(ReportPeriodPreset.Custom, Wd, Wd.AddDays(-1), Wd, out _, out var e2).Should().BeFalse();
        e2.Should().Be("Конец периода раньше начала");
        ReportPeriod.TryResolve(ReportPeriodPreset.Custom, new DateOnly(2025, 9, 29), Wd, Wd, out _, out var e3).Should().BeFalse();
        e3.Should().Be("Период — не длиннее 366 дней");
    }

    [Fact]
    public void FromAndTo_AreIgnoredWithoutCustom() =>
        Resolve(ReportPeriodPreset.Today, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 2)).From.Should().Be(Wd);

    [Fact]
    public void Previous_IsTheSameLengthBefore_AndMonthsAreCalendarMonths()
    {
        var p = Resolve(ReportPeriodPreset.Last7Days).Previous();
        (p.From, p.To).Should().Be((new DateOnly(2026, 9, 17), new DateOnly(2026, 9, 23)));
        var m = Resolve(ReportPeriodPreset.ThisMonth).Previous();
        (m.From, m.To, m.Label).Should().Be((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "Август 2026"));
        var today = Resolve(ReportPeriodPreset.Today).Previous();
        (today.From, today.To).Should().Be((new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 29)));
    }
}
