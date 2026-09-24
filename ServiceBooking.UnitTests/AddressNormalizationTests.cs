using FluentAssertions;
using ServiceBooking.API.Services.Geo;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE13.md §203/§214 — pure, no DB, no network. Every case here is fed OUR OWN
/// text (never a geocoder response) per the function's own contract.</summary>
public class AddressNormalizationTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Key_NullOrWhitespace_ReturnsEmpty(string? input, string expected) =>
        AddressNormalization.Key(input).Should().Be(expected);

    [Fact]
    public void Key_TrimsLeadingAndTrailingWhitespace() =>
        AddressNormalization.Key("  Ленина 5  ").Should().Be("ленина 5");

    [Fact]
    public void Key_CollapsesInternalWhitespaceRuns() =>
        AddressNormalization.Key("Ленина    5,   к1").Should().Be("ленина 5, к1");

    [Fact]
    public void Key_CollapsesTabsAndNewlinesToo() =>
        AddressNormalization.Key("Ленина\t5\nк1").Should().Be("ленина 5 к1");

    [Fact]
    public void Key_LowercasesInvariantly() =>
        AddressNormalization.Key("ЛЕНИНА 5").Should().Be("ленина 5");

    [Fact]
    public void Key_FoldsYo_ToYe() =>
        AddressNormalization.Key("Ёлочная 3").Should().Be("елочная 3");

    [Fact]
    public void Key_FoldsUppercaseYo_ToYe() =>
        AddressNormalization.Key("ЁЛОЧНАЯ 3").Should().Be("елочная 3");

    [Theory]
    [InlineData("Ленина 5.", "ленина 5")]
    [InlineData("Ленина 5,", "ленина 5")]
    [InlineData("Ленина 5..,", "ленина 5")]
    [InlineData("Ленина 5,.,", "ленина 5")]
    public void Key_DropsTrailingPunctuation(string input, string expected) =>
        AddressNormalization.Key(input).Should().Be(expected);

    [Fact]
    public void Key_DoesNotDropInternalPunctuation() =>
        AddressNormalization.Key("Ленина, 5").Should().Be("ленина, 5");

    [Fact]
    public void Key_SameTextDifferentCasingAndSpacing_ProducesSameKey()
    {
        var a = AddressNormalization.Key("  ленина   5 ");
        var b = AddressNormalization.Key("Ленина 5.");
        a.Should().Be(b);
    }

    [Fact]
    public void Key_DifferentText_ProducesDifferentKeys() =>
        AddressNormalization.Key("Ленина 5").Should().NotBe(AddressNormalization.Key("Ленина 6"));
}
