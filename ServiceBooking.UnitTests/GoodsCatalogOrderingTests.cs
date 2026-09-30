using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §505.3 — groups, order, search of the goods catalog.</summary>
public class GoodsCatalogOrderingTests
{
    [Theory]
    [InlineData(true, true, true, CatalogAcceptance.AcceptingNow)]
    [InlineData(true, true, false, CatalogAcceptance.AcceptingNow)]
    [InlineData(true, false, true, CatalogAcceptance.PreorderOnly)]
    [InlineData(true, false, false, CatalogAcceptance.NotAccepting)]
    [InlineData(false, true, true, CatalogAcceptance.NotAccepting)]
    [InlineData(false, false, false, CatalogAcceptance.NotAccepting)]
    public void Classify(bool accepting, bool asap, bool scheduled, CatalogAcceptance expected) =>
        GoodsCatalogOrdering.Classify(accepting, asap, scheduled).Should().Be(expected);

    [Fact]
    public void Texts() =>
        new[] { CatalogAcceptance.AcceptingNow, CatalogAcceptance.PreorderOnly, CatalogAcceptance.NotAccepting }
            .Select(GoodsCatalogOrdering.Text).Should().Equal("Принимает заказы", "Можно заказать заранее", "Временно не принимает заказы");

    private sealed record Shop(string Name, CatalogAcceptance Acceptance, Guid Id);

    [Fact]
    public void Order_IsGroupThenNameInRussianThenId()
    {
        var idLow = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var idHigh = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var shops = new[]
        {
            new Shop("Яблоко", CatalogAcceptance.AcceptingNow, Guid.NewGuid()),
            new Shop("Ёжик", CatalogAcceptance.NotAccepting, Guid.NewGuid()),
            new Shop("Арбуз", CatalogAcceptance.PreorderOnly, Guid.NewGuid()),
            new Shop("Борщ", CatalogAcceptance.AcceptingNow, idHigh),
            new Shop("борщ", CatalogAcceptance.AcceptingNow, idLow),
            new Shop("Аврора", CatalogAcceptance.NotAccepting, Guid.NewGuid()),
        };
        var ordered = GoodsCatalogOrdering.Order(shops, s => s.Acceptance, s => s.Name, s => s.Id);
        // Equal names (ignoring case) fall back to the id.
        ordered.Select(s => s.Name).Should().Equal("борщ", "Борщ", "Яблоко", "Арбуз", "Аврора", "Ёжик");
        ordered.Take(2).Select(s => s.Id).Should().Equal(idLow, idHigh);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" шаурма ", "шаурма")]
    public void NormalizeSearch_Trims(string? input, string? expected) => GoodsCatalogOrdering.NormalizeSearch(input).Should().Be(expected);

    [Fact]
    public void NormalizeSearch_CutsAt100() => GoodsCatalogOrdering.NormalizeSearch(new string('а', 150))!.Length.Should().Be(100);

    [Theory]
    [InlineData("Шаурма на Ленина", null, "ШАУРМА", true)]
    [InlineData("Пекарня", "ул. Ленина, 5", "ленина", true)]
    [InlineData("Пекарня", "ул. Ленина, 5", "мира", false)]
    [InlineData("Пекарня", null, "мира", false)]
    public void Matches_IsCaseInsensitive_InNameAndAddress(string name, string? address, string search, bool expected) =>
        GoodsCatalogOrdering.Matches(name, address, search).Should().Be(expected);

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(4, 4)]
    public void NormalizePage(int? page, int expected) => GoodsCatalogOrdering.NormalizePage(page).Should().Be(expected);
}
