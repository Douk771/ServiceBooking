using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §34, US-30 p.3 — the three PUT rules plus the POST case, all pure.</summary>
public class CompanyTimeZoneResolverTests
{
    // ── ForNewCompany ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForNewCompany_NoRequestedZone_UsesCityZone_NotManual()
    {
        var result = CompanyTimeZoneResolver.ForNewCompany("Asia/Barnaul", requestedTimeZoneId: null);
        result.Should().Be(new CompanyTimeZoneResolver.Result("Asia/Barnaul", false));
    }

    [Fact]
    public void ForNewCompany_RequestedZoneMatchesCity_NotManual()
    {
        var result = CompanyTimeZoneResolver.ForNewCompany("Asia/Barnaul", "Asia/Barnaul");
        result.Should().Be(new CompanyTimeZoneResolver.Result("Asia/Barnaul", false));
    }

    [Fact]
    public void ForNewCompany_RequestedZoneDiffersFromCity_IsManual()
    {
        var result = CompanyTimeZoneResolver.ForNewCompany("Asia/Barnaul", "Europe/Moscow");
        result.Should().Be(new CompanyTimeZoneResolver.Result("Europe/Moscow", true));
    }

    // ── ForUpdate: rule 1 — only cityId changes ─────────────────────────────────────────────────────

    [Fact]
    public void ForUpdate_OnlyCityChanged_NotManual_ZoneFollowsNewCity()
    {
        var result = CompanyTimeZoneResolver.ForUpdate(
            effectiveCityTimeZoneId: "Asia/Vladivostok", cityChanged: true,
            timeZoneIdFieldProvided: false, requestedTimeZoneId: null,
            currentTimeZoneId: "Asia/Barnaul", currentIsManual: false);

        result.Should().Be(new CompanyTimeZoneResolver.Result("Asia/Vladivostok", false));
    }

    [Fact]
    public void ForUpdate_OnlyCityChanged_AlreadyManual_ZoneDoesNotFollow()
    {
        // US-30 p.3: a manual override survives a later city save that doesn't touch the zone field.
        var result = CompanyTimeZoneResolver.ForUpdate(
            effectiveCityTimeZoneId: "Asia/Vladivostok", cityChanged: true,
            timeZoneIdFieldProvided: false, requestedTimeZoneId: null,
            currentTimeZoneId: "Europe/Kaliningrad", currentIsManual: true);

        result.Should().Be(new CompanyTimeZoneResolver.Result("Europe/Kaliningrad", true));
    }

    [Fact]
    public void ForUpdate_NothingChanged_ReturnsCurrentUnchanged()
    {
        var result = CompanyTimeZoneResolver.ForUpdate(
            effectiveCityTimeZoneId: "Asia/Barnaul", cityChanged: false,
            timeZoneIdFieldProvided: false, requestedTimeZoneId: null,
            currentTimeZoneId: "Asia/Barnaul", currentIsManual: false);

        result.Should().Be(new CompanyTimeZoneResolver.Result("Asia/Barnaul", false));
    }

    // ── ForUpdate: rule 2 — timeZoneId given, differs from city zone ───────────────────────────────

    [Fact]
    public void ForUpdate_TimeZoneIdDiffersFromCityZone_BecomesManual()
    {
        var result = CompanyTimeZoneResolver.ForUpdate(
            effectiveCityTimeZoneId: "Asia/Barnaul", cityChanged: false,
            timeZoneIdFieldProvided: true, requestedTimeZoneId: "Europe/Moscow",
            currentTimeZoneId: "Asia/Barnaul", currentIsManual: false);

        result.Should().Be(new CompanyTimeZoneResolver.Result("Europe/Moscow", true));
    }

    [Fact]
    public void ForUpdate_TimeZoneIdEqualsCityZone_NotManual()
    {
        var result = CompanyTimeZoneResolver.ForUpdate(
            effectiveCityTimeZoneId: "Asia/Barnaul", cityChanged: false,
            timeZoneIdFieldProvided: true, requestedTimeZoneId: "Asia/Barnaul",
            currentTimeZoneId: "Europe/Moscow", currentIsManual: true);

        result.Should().Be(new CompanyTimeZoneResolver.Result("Asia/Barnaul", false));
    }

    // ── ForUpdate: rule 3 — explicit null reverts to city zone ─────────────────────────────────────

    [Fact]
    public void ForUpdate_ExplicitNullTimeZoneId_RevertsToCityZone_ClearsManual()
    {
        var result = CompanyTimeZoneResolver.ForUpdate(
            effectiveCityTimeZoneId: "Asia/Barnaul", cityChanged: false,
            timeZoneIdFieldProvided: true, requestedTimeZoneId: null,
            currentTimeZoneId: "Europe/Moscow", currentIsManual: true);

        result.Should().Be(new CompanyTimeZoneResolver.Result("Asia/Barnaul", false));
    }

    [Fact]
    public void ForUpdate_ExplicitNull_FieldOmitted_AreDifferentOutcomes()
    {
        // The whole reason ForUpdate takes a separate "field provided" flag: `requestedTimeZoneId ==
        // null` alone can't tell these two requests apart, and they must behave differently.
        var omitted = CompanyTimeZoneResolver.ForUpdate(
            "Asia/Barnaul", cityChanged: false,
            timeZoneIdFieldProvided: false, requestedTimeZoneId: null,
            currentTimeZoneId: "Europe/Moscow", currentIsManual: true);

        var explicitNull = CompanyTimeZoneResolver.ForUpdate(
            "Asia/Barnaul", cityChanged: false,
            timeZoneIdFieldProvided: true, requestedTimeZoneId: null,
            currentTimeZoneId: "Europe/Moscow", currentIsManual: true);

        omitted.Should().Be(new CompanyTimeZoneResolver.Result("Europe/Moscow", true)); // unchanged
        explicitNull.Should().Be(new CompanyTimeZoneResolver.Result("Asia/Barnaul", false)); // reverted
    }
}

/// <summary>ARCHITECTURE_CYCLE4.md §31.1/§31.4 — utcOffsetMinutes.</summary>
public class TimeZoneOffsetTests
{
    private static readonly DateTime SummerUtc = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Europe/Kaliningrad", 120)]
    [InlineData("Europe/Moscow", 180)]
    [InlineData("Asia/Barnaul", 420)]
    [InlineData("Asia/Vladivostok", 600)]
    public void TryGetUtcOffsetMinutes_KnownZones_ReturnsExpectedOffset(string zone, int expectedMinutes)
    {
        var ok = TimeZoneOffset.TryGetUtcOffsetMinutes(zone, SummerUtc, out var minutes);

        ok.Should().BeTrue();
        minutes.Should().Be(expectedMinutes);
    }

    [Fact]
    public void TryGetUtcOffsetMinutes_UnknownZone_ReturnsFalse()
    {
        var ok = TimeZoneOffset.TryGetUtcOffsetMinutes("Not/AZone", SummerUtc, out var minutes);

        ok.Should().BeFalse();
        minutes.Should().Be(0);
    }
}
