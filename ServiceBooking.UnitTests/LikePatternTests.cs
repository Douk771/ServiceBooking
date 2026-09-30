using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

public class LikePatternTests
{
    [Theory]
    [InlineData("анна", "%анна%")]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("a\\b", "%a\\\\b%")]
    public void Contains_EscapesWildcards(string input, string expected) => LikePattern.Contains(input).Should().Be(expected);
}
