using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.UnitTests.Cycle48;

/// <summary>CY48-B1 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1): the embedded single source of operator requisites.</summary>
public class OperatorPublicRequisitesTests
{
    [Fact]
    public void EmbeddedResource_LoadsAllThirteenValues_EqualToTheFileOnDisk()
    {
        using var file = ContractFiles.Load("legal", "operator-public.json");
        var onDisk = file.RootElement.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());

        OperatorPublicRequisites.Values.Should().HaveCount(13);
        OperatorPublicRequisites.Values.Should().BeEquivalentTo(onDisk);
        OperatorPublicRequisites.SharedWithLegalValues.Should().HaveCount(8)
            .And.BeEquivalentTo(file.RootElement.GetProperty("sharedWithLegalValues").EnumerateArray().Select(e => e.GetString()));
        OperatorPublicRequisites.SharedWithLegalValues.Should().OnlyContain(k => OperatorPublicRequisites.Values.ContainsKey(k));
    }

    [Fact]
    public void Get_ReturnsValue_AndThrowsOnUnknownKey()
    {
        OperatorPublicRequisites.Get("ПОЧТА_ПОДДЕРЖКИ").Should().Be(OperatorPublicRequisites.Values["ПОЧТА_ПОДДЕРЖКИ"]);
        var act = () => OperatorPublicRequisites.Get("НЕТ_ТАКОГО");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Fill_SubstitutesKnownKeys_AndTelDerivative()
    {
        var result = OperatorPublicRequisites.Fill("Срок: {{СРОК_СЧЁТА}}; активация: {{СРОК_АКТИВАЦИИ}}; тел {{ТЕЛЕФОН_TEL}}. $1 {0}");

        result.Should().Be(
            $"Срок: {OperatorPublicRequisites.Get("СРОК_СЧЁТА")}; активация: {OperatorPublicRequisites.Get("СРОК_АКТИВАЦИИ")}; "
            + $"тел {OperatorPublicRequisites.TelHref()[4..]}. $1 {{0}}");
        result.Should().NotContain("{{");
    }

    [Fact]
    public void Fill_UnknownKey_Throws()
    {
        var act = () => OperatorPublicRequisites.Fill("до {{СРОК_НЕИЗВЕСТЕН}}");
        act.Should().Throw<InvalidOperationException>().WithMessage("*СРОК_НЕИЗВЕСТЕН*");
    }

    [Fact]
    public void Fill_TemplateWithoutPlaceholders_IsUnchanged() =>
        OperatorPublicRequisites.Fill("Без подстановок").Should().Be("Без подстановок");

    [Fact]
    public void TelHref_IsPlusAndAllDigitsOfThePhone()
    {
        var digits = new string(OperatorPublicRequisites.Get("ТЕЛЕФОН_ОПЕРАТОРА").Where(char.IsAsciiDigit).ToArray());

        OperatorPublicRequisites.TelHref().Should().Be("tel:+" + digits);
        OperatorPublicRequisites.TelHref().Should().MatchRegex(@"^tel:\+7\d{10}$");
    }

    [Fact]
    public void Values_AreWithoutPlaceholdersLeftToFill()
    {
        // DO-48-1 filled the four "ЗАПОЛНИТЬ" markers; none may come back.
        OperatorPublicRequisites.Values.Values.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v) && !v.Contains("ЗАПОЛНИТЬ") && !Regex.IsMatch(v, @"\{\{"));
    }
}
