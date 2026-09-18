using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE4.md §19.5's exact example shape.</summary>
public class PhoneDisplayMaskTests
{
    [Fact]
    public void Mask_RussianNumber_MatchesContractExampleShape()
    {
        PhoneDisplayMask.Mask("79991234545").Should().Be("+7 999 ***-**-45");
    }

    [Fact]
    public void Mask_NullOrEmpty_ReturnsEmpty()
    {
        PhoneDisplayMask.Mask(null).Should().BeEmpty();
        PhoneDisplayMask.Mask("").Should().BeEmpty();
    }

    [Fact]
    public void Mask_NeverRevealsMoreThanLastTwoDigits_ForNonRuShape()
    {
        var masked = PhoneDisplayMask.Mask("12345"); // 5 digits, not the 11-digit RU shape
        masked.Should().EndWith("45");
        masked.Should().NotContain("123");
    }

    [Fact]
    public void Mask_VeryShortInput_IsFullyMasked()
    {
        PhoneDisplayMask.Mask("123").Should().Be("***");
    }
}
