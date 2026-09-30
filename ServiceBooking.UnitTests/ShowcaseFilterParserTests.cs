using FluentAssertions;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE28.md §594.1 — the <c>showcase</c> parameter of the admin lists.</summary>
public class ShowcaseFilterParserTests
{
    [Theory]
    [InlineData(null, ShowcaseFilter.All)]
    [InlineData("", ShowcaseFilter.All)]
    [InlineData("   ", ShowcaseFilter.All)]
    [InlineData("all", ShowcaseFilter.All)]
    [InlineData("only", ShowcaseFilter.Only)]
    [InlineData("exclude", ShowcaseFilter.Exclude)]
    [InlineData("ONLY", ShowcaseFilter.Only)]
    [InlineData(" Exclude ", ShowcaseFilter.Exclude)]
    public void TryParse_AcceptsTheThreeValues_AndTreatsAbsenceAsAll(string? raw, ShowcaseFilter expected)
    {
        ShowcaseFilterParser.TryParse(raw, out var filter).Should().BeTrue();
        filter.Should().Be(expected);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("true")]
    [InlineData("only,exclude")]
    [InlineData("0")]
    public void TryParse_RefusesAnythingElse(string raw) =>
        ShowcaseFilterParser.TryParse(raw, out _).Should().BeFalse();

    [Fact]
    public void InvalidText_IsTheContractText() =>
        ShowcaseFilterParser.InvalidText.Should().Be("showcase должен быть одним из: all, only, exclude.");
}
