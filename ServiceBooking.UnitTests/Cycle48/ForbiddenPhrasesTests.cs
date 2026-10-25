using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Showcase.Tariffs;

namespace ServiceBooking.UnitTests.Cycle48;

/// <summary>CY48-B10: the regular expressions of contracts/cycle48/forbidden-phrases.json work in .NET, and the tariff grids are clean.</summary>
public class ForbiddenPhrasesTests
{
    [Fact]
    public void AllPatterns_CompileInDotNet()
    {
        using var doc = ContractFiles.Load("cycle48", "forbidden-phrases.json");
        foreach (var item in doc.RootElement.GetProperty("patterns").EnumerateArray())
        {
            var options = item.GetProperty("ignoreCase").GetBoolean() ? RegexOptions.IgnoreCase : RegexOptions.None;
            var create = () => new Regex(item.GetProperty("regex").GetString()!, options | RegexOptions.CultureInvariant);
            create.Should().NotThrow(item.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void TariffScope_IsNotEmpty() => TariffForbiddenPhrases.PatternIds().Should().HaveCountGreaterThanOrEqualTo(6);

    [Theory]
    [InlineData("Цена не зависит от числа филиалов")]
    [InlineData("Цена не зависит от числа магазинов")]
    [InlineData("Цена не зависит от числа гостей")]
    [InlineData("— от 690 ₽/мес")]
    public void AllowedStrings_AreNotCaught(string text)
    {
        // "— от 690 ₽/мес" is the price line of a landing block, caught only in scope tariffTexts by "from-price" — so check the other scope explicitly.
        if (text.Contains("690")) TariffForbiddenPhrases.FindFirst(text, "publicCopy").Should().BeNull();
        else TariffForbiddenPhrases.FindFirst(text).Should().BeNull();
    }

    [Theory]
    [InlineData("Стоимость от 3 900 ₽ в месяц", "от 3 900 ₽")]
    [InlineData("От 2 990 ₽", "От 2 990 ₽")]
    [InlineData("Итоговая цена — После заявки", "После заявки")]
    [InlineData("мы свяжемся с вами", "мы свяжемся с вами")]
    [InlineData("Цена зависит от числа филиалов", "зависит от числа филиалов")]
    [InlineData("по числу магазинов", "по числу магазинов")]
    [InlineData("остался {{КЛЮЧ}}", "{{")]
    public void OldWording_IsCaught(string text, string match) => TariffForbiddenPhrases.FindFirst(text).Should().Be(match);

    [Fact]
    public void TariffGrids_AreClean()
    {
        var texts = ZapisTariffCatalog.Grid.SelectMany(t => t.Highlights.Append(t.Description))
            .Concat(OrdersTariffCatalog.Grid.SelectMany(t => t.Highlights.Append(t.Description)));

        foreach (var text in texts)
            TariffForbiddenPhrases.FindFirst(text).Should().BeNull($"«{text}» is shown on the public price list");
    }

    [Fact]
    public void LegacyConstants_AreCaught_SoTheWarningWorks()
    {
        TariffForbiddenPhrases.FindFirst(ZapisTariffCatalog.LegacyNetworkDescription).Should().NotBeNull();
        TariffForbiddenPhrases.FindFirst(ZapisTariffCatalog.LegacyNetworkHighlights).Should().NotBeNull();
        TariffForbiddenPhrases.FindFirst(OrdersTariffCatalog.LegacyChainDescription).Should().NotBeNull();
        TariffForbiddenPhrases.FindFirst(OrdersTariffCatalog.LegacyChainHighlights).Should().NotBeNull();
    }
}
