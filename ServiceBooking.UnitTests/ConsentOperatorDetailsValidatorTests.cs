using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §402.5, API_CONTRACT_CYCLE20.md §432.8 (Т20-04 п. 3) — pure, no EF/HTTP.
/// </summary>
public class ConsentOperatorDetailsValidatorTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" Иванова Мария Сергеевна ", "Иванова Мария Сергеевна")]
    public void Normalize_BlankBecomesNull_OtherwiseTrimmed(string? input, string? expected) =>
        ConsentOperatorDetailsValidator.Normalize(input).Should().Be(expected);

    [Fact]
    public void Validate_AllNull_Ok() =>
        ConsentOperatorDetailsValidator.Validate(null, null, null).Should().BeNull();

    [Fact]
    public void Validate_WellFormedTriple_Ok() =>
        ConsentOperatorDetailsValidator.Validate("Иванова Мария Сергеевна", "г. Барнаул, ул. Ленина, 1", "222500000000").Should().BeNull();

    [Fact]
    public void Validate_FullNameTooLong_Rejected() =>
        ConsentOperatorDetailsValidator.Validate(new string('А', 301), null, null).Should().NotBeNull();

    [Fact]
    public void Validate_FullNameAtMaxLength_Ok() =>
        ConsentOperatorDetailsValidator.Validate(new string('А', 300), null, null).Should().BeNull();

    [Theory]
    [InlineData("Иванов И. И.")]
    [InlineData("Иванов И.И.")]
    [InlineData("И. Иванов")]
    [InlineData("Smith J.")]
    public void Validate_InitialsInsteadOfFullName_Rejected(string fullName) =>
        ConsentOperatorDetailsValidator.Validate(fullName, null, null).Should().NotBeNull();

    [Fact]
    public void Validate_FullNameSpelledOut_Ok() =>
        ConsentOperatorDetailsValidator.Validate("Иванов Иван Иванович", null, null).Should().BeNull();

    [Fact]
    public void Validate_AddressTooLong_Rejected() =>
        ConsentOperatorDetailsValidator.Validate(null, new string('а', 501), null).Should().NotBeNull();

    [Fact]
    public void Validate_AddressAtMaxLength_Ok() =>
        ConsentOperatorDetailsValidator.Validate(null, new string('а', 500), null).Should().BeNull();

    [Theory]
    [InlineData("1234567890")] // 10 digits
    [InlineData("123456789012")] // 12 digits
    public void Validate_InnWithAllowedLength_Ok(string inn) =>
        ConsentOperatorDetailsValidator.Validate(null, null, inn).Should().BeNull();

    [Theory]
    [InlineData("123456789")] // 9 digits
    [InlineData("12345678901")] // 11 digits
    [InlineData("12345678901a")] // non-digit
    [InlineData(" 1234567890")] // leading space
    public void Validate_InnWithWrongShape_Rejected(string inn) =>
        ConsentOperatorDetailsValidator.Validate(null, null, inn).Should().NotBeNull();

    [Fact]
    public void IsMissing_BothPresent_False() =>
        ConsentOperatorDetailsValidator.IsMissing("Иванова Мария Сергеевна", "г. Барнаул").Should().BeFalse();

    [Fact]
    public void IsMissing_NoFullName_True() =>
        ConsentOperatorDetailsValidator.IsMissing(null, "г. Барнаул").Should().BeTrue();

    [Fact]
    public void IsMissing_NoAddress_True() =>
        ConsentOperatorDetailsValidator.IsMissing("Иванова Мария Сергеевна", null).Should().BeTrue();

    [Fact]
    public void IsMissing_InnAloneDoesNotCount_StillMissingWithoutNameAndAddress() =>
        ConsentOperatorDetailsValidator.IsMissing(null, null).Should().BeTrue();
}
