using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §34.2 — must match City.SearchName's seeding rule exactly.</summary>
public class CitySearchTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Барнаул", "барнаул")]
    [InlineData("БАРНАУЛ", "барнаул")]
    [InlineData("Йошкар-Ола", "йошкарола")]
    [InlineData("Ростов-на-Дону", "ростовнадону")]
    [InlineData(" Москва ", "москва")]
    [InlineData("Орёл", "орел")]
    [InlineData("ёлки-ёлки", "елкиелки")]
    public void Normalize_MatchesSeedingRule(string? input, string expected) =>
        CitySearch.Normalize(input).Should().Be(expected);

    // Cycle-07 QA finding #2: GET /api/cities?search=... 500ed on an embedded NUL byte reaching
    // EF.Functions.ILike (Postgres' `text` type rejects it outright).
    [Fact]
    public void Normalize_StripsEmbeddedNulByte() =>
        CitySearch.Normalize("Моск\0ва").Should().Be("москва");

    [Fact]
    public void Normalize_StripsOtherControlCharacters() =>
        CitySearch.Normalize("Казань").Should().Be("казань");
}
