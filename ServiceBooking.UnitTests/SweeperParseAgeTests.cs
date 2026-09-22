using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Покрывает чистую функцию <see cref="Sweeper.ParseAge"/> (§86.1 API_CONTRACT_CYCLE8.md). Ни Docker,
/// ни БД, ни процесс не запускаются — только разбор строки.
/// </summary>
public class SweeperParseAgeTests
{
    [Theory]
    [InlineData("30m", 30 * 60)]
    [InlineData("2h", 2 * 3600)]
    [InlineData("1d", 24 * 3600)]
    [InlineData("0m", 0)]
    public void ParseAge_accepts_documented_units(string input, int expectedSeconds)
    {
        Sweeper.ParseAge(input).TotalSeconds.Should().Be(expectedSeconds);
    }

    [Theory]
    [InlineData("90s")]
    [InlineData("2 hours")]
    [InlineData("")]
    [InlineData("h2")]
    [InlineData("abc")]
    public void ParseAge_rejects_malformed_values_with_a_safety_exception_not_a_crash(string input)
    {
        // Review blocker: an unhandled exception here used to escape RunAsync as a raw stack trace
        // instead of the documented exit code 1 — ParseAge itself must always throw the typed
        // exception, never let int.TryParse's absence of exceptions mask a bad value as "accepted".
        var act = () => Sweeper.ParseAge(input);

        act.Should().Throw<TestSafetyException>();
    }
}
