using FluentAssertions;
using ServiceBooking.API.Services.Slots;
using Xunit;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE42.md §42.30.2: words of the contract list, beyond the shared vectors.</summary>
public class OwnerTextChecksContractTests
{
    [Theory]
    [InlineData("Оздоровление организма", "HealthClaim")]
    [InlineData("Иммунитет и польза", "HealthClaim")]
    [InlineData("Противопоказаний нет", "HealthClaim")]
    [InlineData("500 ₽ за каждого гостя", "MandatoryExtraCharge")]
    [InlineData("300 рублей за человека", "MandatoryExtraCharge")]
    [InlineData("Обязательно возьмите простыню", "MandatoryExtraCharge")]
    [InlineData("Невозврат при отмене", "CancellationTermsInText")]
    [InlineData("Карта 4276 1234 5678 9012 345", "CardNumber")]
    [InlineData("Карта 1234567890123", "CardNumber")]
    [InlineData("Карта 1234-5678-9012-3456-789", "CardNumber")]
    public void Warns(string text, string code) => OwnerTextChecks.Check(text).Warnings.Should().Contain(code);

    [Theory]
    [InlineData("Противопоказания: гипертония, беременность")]
    [InlineData("Укажите противопоказания заранее")]
    [InlineData("Звоните 8 900 123 45 67")]
    [InlineData("Баня сверху, парная внизу")]
    public void DoesNotWarn(string text) => OwnerTextChecks.Check(text).Warnings.Should().BeEmpty();
}
