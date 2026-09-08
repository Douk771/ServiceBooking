using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

public class LogMaskingTests
{
    [Theory]
    [InlineData("79991234567", "7999***4567")]  // canonical RU number, ARCHITECTURE.md §11.3 example
    [InlineData("380671234567", "3806***4567")] // international, 12 digits
    [InlineData("123456789012345", "1234***2345")] // 15 digits, upper E.164 bound
    public void Phone_TypicalCanonicalNumber_KeepsFirstAndLastFourDigits(string input, string expected) =>
        LogMasking.Phone(input).Should().Be(expected);

    [Theory]
    [InlineData("12345678")]  // exactly 8 — keeping 4+4 would leave nothing masked, so mask fully instead
    [InlineData("1234567")]
    [InlineData("1")]
    public void Phone_TooShortToLeaveAGap_IsMaskedCompletely(string input) =>
        LogMasking.Phone(input).Should().Be(new string('*', input.Length));

    [Fact]
    public void Phone_Null_ReturnsNull() =>
        LogMasking.Phone(null).Should().BeNull();

    [Fact]
    public void Phone_Empty_ReturnsEmpty() =>
        LogMasking.Phone("").Should().BeEmpty();

    [Fact]
    public void Phone_NeverContainsTheOriginalMiddleDigits()
    {
        const string phone = "79991234567";
        var masked = LogMasking.Phone(phone);

        masked.Should().NotContain("123456"); // the masked middle segment must not survive verbatim
    }

    [Theory]
    [InlineData("Booking created for phone 79991234567, id abc123")]
    [InlineData("Обработка платежа для 79991234567 успешна")]
    public void MaskPhoneSequences_TextContainingAContiguousPhoneDigitRun_MasksIt(string text)
    {
        var masked = LogMasking.MaskPhoneSequences(text);
        masked.Should().NotBe(text);
        masked.Should().Contain("***");
        masked.Should().NotContain("79991234567");
    }

    [Theory]
    [InlineData("Price: 1500, quantity: 3")]        // ordinary short numbers must survive untouched
    [InlineData("Order #42 completed in 250ms")]
    [InlineData("No digits here at all")]
    public void MaskPhoneSequences_DoesNotTouchOrdinaryShortNumbers(string text) =>
        LogMasking.MaskPhoneSequences(text).Should().Be(text);

    [Fact]
    public void MaskPhoneSequences_MultiplePhonesInOneMessage_MasksBoth()
    {
        var masked = LogMasking.MaskPhoneSequences("Merged 79991234567 into 79997654321");

        masked.Should().NotContain("79991234567");
        masked.Should().NotContain("79997654321");
        masked.Should().Contain("7999***4567");
        masked.Should().Contain("7999***4321");
    }

    [Fact]
    public void MaskPhoneSequences_SixteenDigitRun_IsNotTreatedAsAPhone()
    {
        // Outside PhoneNormalizer's 10-15 digit E.164 bounds — e.g. a card number or long numeric id;
        // masking it would be both wrong (it isn't a phone) and would hide the actual value from logs
        // that legitimately need it.
        const string text = "Reference 1234567890123456";
        LogMasking.MaskPhoneSequences(text).Should().Be(text);
    }
}
