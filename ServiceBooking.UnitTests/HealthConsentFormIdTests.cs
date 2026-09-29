using FluentAssertions;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §402.5 (US-20-01) — pure, no EF/HTTP.
/// </summary>
public class HealthConsentFormIdTests
{
    [Fact]
    public void New_MatchesTheContractFormat()
    {
        for (var i = 0; i < 200; i++)
            HealthConsentFormId.New().Should().MatchRegex("^HD-[0-9A-HJKMNP-TV-Z]{8}$");
    }

    [Fact]
    public void New_IsValid_AlwaysAgreesWithItself()
    {
        for (var i = 0; i < 200; i++)
            HealthConsentFormId.IsValid(HealthConsentFormId.New()).Should().BeTrue();
    }

    [Fact]
    public void New_NeverContainsExcludedLetters()
    {
        // Crockford base32: I, L, O, U are excluded (confusable with 1/1/0/V).
        for (var i = 0; i < 200; i++)
            HealthConsentFormId.New().Should().NotContainAny("I", "L", "O", "U");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HD-7K3M9QT")] // 7 chars
    [InlineData("HD-7K3M9QTXX")] // 9 chars
    [InlineData("hd-7K3M9QTX")] // lowercase prefix
    [InlineData("XX-7K3M9QTX")] // wrong prefix
    [InlineData("HD-7K3M9QTI")] // contains excluded letter I
    [InlineData("HD-7K3M9QTL")] // contains excluded letter L
    [InlineData("HD-7K3M9QTO")] // contains excluded letter O
    [InlineData("HD-7K3M9QTU")] // contains excluded letter U
    [InlineData("HD 7K3M9QTX")] // missing dash
    public void IsValid_RejectsMalformedInput(string? value) => HealthConsentFormId.IsValid(value).Should().BeFalse();

    [Fact]
    public void IsValid_AcceptsAWellFormedId() => HealthConsentFormId.IsValid("HD-7K3M9QTX").Should().BeTrue();
}
