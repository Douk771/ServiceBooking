using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("+7 999 000-00-00", "79990000000")]
    [InlineData("8 (999) 000 00 00", "79990000000")]
    [InlineData("79990000000", "79990000000")]
    [InlineData("9990000000", "79990000000")]
    [InlineData("  +7 999 000 00 00  ", "79990000000")]
    [InlineData("+380 67 123 45 67", "380671234567")]
    [InlineData("", "")]
    public void Normalize_ProducesDigitsOnlyCanonicalForm(string input, string expected) =>
        PhoneNormalizer.Normalize(input).Should().Be(expected);

    [Fact]
    public void Normalize_NullInput_ReturnsEmptyString() =>
        PhoneNormalizer.Normalize(null).Should().BeEmpty();

    [Theory]
    [InlineData("89990000000", true)]   // 11 digits starting with 8 → valid RU number
    [InlineData("9990000000", true)]    // 10 digits starting with 9 → padded to 11
    [InlineData("380671234567", true)]  // international, kept as-is, within bounds
    [InlineData("123", false)]          // too short after normalization
    [InlineData("1234567890123456", false)] // too long (16 digits)
    public void TryNormalize_ValidatesE164Bounds(string input, bool expectedValid)
    {
        var ok = PhoneNormalizer.TryNormalize(input, out _);
        ok.Should().Be(expectedValid);
    }

    [Fact]
    public void TryNormalize_LettersOnly_IsInvalid()
    {
        var ok = PhoneNormalizer.TryNormalize("not-a-phone", out var canonical);
        ok.Should().BeFalse();
        canonical.Should().BeEmpty();
    }

    [Fact]
    public void TryNormalize_MixedLettersAndDigits_KeepsOnlyDigitsAndValidatesLength()
    {
        // "call 8999abc0000" -> digits "89990000" (8 digits) -> too short
        var ok = PhoneNormalizer.TryNormalize("call 8999abc0000", out var canonical);
        canonical.Should().Be("89990000");
        ok.Should().BeFalse();
    }

    [Fact]
    public void Normalize_ElevenDigitsStartingWithEightBecomesSeven()
    {
        PhoneNormalizer.Normalize("88005553535").Should().Be("78005553535");
    }

    [Fact]
    public void Normalize_TenDigitsStartingWithNineIsPaddedWithSeven()
    {
        PhoneNormalizer.Normalize("9005553535").Should().Be("79005553535");
    }

    [Fact]
    public void Normalize_ElevenDigitsStartingWithSevenIsUnchanged()
    {
        PhoneNormalizer.Normalize("79990000000").Should().Be("79990000000");
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var once = PhoneNormalizer.Normalize("8 999 000-00-00");
        var twice = PhoneNormalizer.Normalize(once);
        twice.Should().Be(once);
    }

    // ── Non-ASCII digits (code review finding, round 3) ──────────────────────
    // Guards the exact bug the class doc's ASCII-only comment explains: char.IsDigit is Unicode-aware
    // and would also accept these as digits, silently disagreeing with the SQL transliteration (which
    // only strips the ASCII '[^0-9]' class) used once in the NormalizePhoneNumbers migration.

    [Fact]
    public void Normalize_ArabicIndicDigits_AreNotTreatedAsCanonicalDigits()
    {
        // "٩٩٩٠٠٠٠٠٠٠" is "9990000000" written in Arabic-Indic digits — char.IsDigit('٩') is true, but
        // these must NOT survive Normalize; only ASCII 0-9 do.
        PhoneNormalizer.Normalize("٩٩٩٠٠٠٠٠٠٠").Should().BeEmpty();
    }

    [Fact]
    public void Normalize_DevanagariDigitsMixedWithAsciiDigits_KeepsOnlyTheAsciiOnes()
    {
        // "9" (ASCII) followed by "९९९" (Devanagari for "999") followed by "0000000" (ASCII) — only the
        // ASCII digits should end up in the canonical form.
        PhoneNormalizer.Normalize("9९९९0000000").Should().Be("90000000");
    }

    [Fact]
    public void TryNormalize_ArabicIndicDigitsOnly_IsInvalid()
    {
        // Without this guard, a search/registration value made entirely of non-ASCII digits would
        // normalize to "" — see the related AdminController phone-search fix (round 3, minor finding).
        var ok = PhoneNormalizer.TryNormalize("٩٩٩٠٠٠٠٠٠٠", out var canonical);
        ok.Should().BeFalse();
        canonical.Should().BeEmpty();
    }

    // ── IsRussian / TryNormalizeRussian (ARCHITECTURE_CYCLE6.md §48.1, US-61/Q4) ──────────────────

    [Fact]
    public void IsRussian_CanonicalRussianNumber_IsTrue() =>
        PhoneNormalizer.IsRussian("79990000000").Should().BeTrue();

    [Theory]
    [InlineData("9990000000")]   // 10 digits
    [InlineData("799900000001")] // 12 digits
    [InlineData("38990000000")]  // 11 digits but doesn't start with '7'
    public void IsRussian_WrongLengthOrPrefix_IsFalse(string canonical) =>
        PhoneNormalizer.IsRussian(canonical).Should().BeFalse();

    [Fact]
    public void TryNormalizeRussian_LeadingEight_NormalizesAndAccepts()
    {
        var ok = PhoneNormalizer.TryNormalizeRussian("8 999 000-00-00", out var canonical);
        ok.Should().BeTrue();
        canonical.Should().Be("79990000000");
    }

    [Fact]
    public void TryNormalizeRussian_ForeignNumber_IsRejected()
    {
        var ok = PhoneNormalizer.TryNormalizeRussian("+380671234567", out var canonical);
        ok.Should().BeFalse();
        canonical.Should().Be("380671234567"); // still normalized, just not accepted
    }
}
