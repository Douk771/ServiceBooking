using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE23.md §390 — suggestion of a shop address from its name.</summary>
public class SlugTransliteratorTests
{
    [Theory]
    [InlineData("Шаурма на Ленина", "shaurma-na-lenina")]
    [InlineData("Ёжик & Хлеб", "yozhik-khleb")]
    [InlineData("Щи-Борщи", "shchi-borshchi")]
    [InlineData("  Кафе «Юг»  ", "kafe-yug")]
    [InlineData("Объём", "obyom")]
    [InlineData("Coffee 24/7", "coffee-24-7")]
    [InlineData("", "")]
    [InlineData("!!!", "")]
    public void Slugify(string name, string expected) => SlugTransliterator.Slugify(name).Should().Be(expected);

    [Fact]
    public void ToBase_EmptyName_FallsBackToShop() =>
        SlugTransliterator.ToBase("!!!", 50, 3, SlugPolicy.ReservedSlugs).Should().Be("magazin");

    [Fact]
    public void ToBase_TooShort_GetsSuffix() =>
        SlugTransliterator.ToBase("Я", 50, 3, SlugPolicy.ReservedSlugs).Should().Be("ya-magazin");

    [Fact]
    public void ToBase_ReservedWordExactly_GetsSuffix() =>
        SlugTransliterator.ToBase("Orders", 50, 3, SlugPolicy.ReservedSlugs).Should().Be("orders-magazin");

    [Fact]
    public void ToBase_LongName_LeavesRoomForSuffix_AndEveryCandidateIsValid()
    {
        var name = string.Join(' ', Enumerable.Repeat("очень длинное название магазина", 4));
        var baseSlug = SlugTransliterator.ToBase(name, SlugPolicy.MaxLength, SlugPolicy.MinLength, SlugPolicy.ReservedSlugs);
        foreach (var candidate in SlugTransliterator.Candidates(baseSlug, () => "a1b2"))
            SlugPolicy.Validate(candidate).Should().Be(SlugCheck.Ok, candidate);
    }

    [Fact]
    public void Candidates_AreBaseThenNumberedThenRandom()
    {
        var list = SlugTransliterator.Candidates("shop", () => "ffff").ToList();
        list.Should().HaveCount(1 + 19 + 1);
        list[0].Should().Be("shop");
        list[1].Should().Be("shop-2");
        list[19].Should().Be("shop-20");
        list[^1].Should().Be("shop-ffff");
    }
}
