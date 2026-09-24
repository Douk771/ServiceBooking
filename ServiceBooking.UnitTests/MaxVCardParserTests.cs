using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE14.md §147.3, §156.1 — multiple TEL, TYPE=CELL ordering, garbage/unusable
/// numbers, no TEL at all, and the byte/count ceilings.</summary>
public class MaxVCardParserTests
{
    private const int DefaultMaxBytes = 16384;

    [Fact]
    public void ExtractPhones_SingleTel_ReturnsCanonicalNumber()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL:+7 (999) 000-00-00\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().ContainSingle().Which.Should().Be("79990000000");
    }

    [Fact]
    public void ExtractPhones_CellTypeComesBeforeUntyped()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL:+79991111111\nTEL;TYPE=CELL:+79992222222\nEND:VCARD";
        var phones = MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes);
        phones.Should().Equal("79992222222", "79991111111");
    }

    [Fact]
    public void ExtractPhones_MobileTypeAlsoTreatedAsCell()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL;TYPE=MOBILE,VOICE:+79990000000\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().ContainSingle().Which.Should().Be("79990000000");
    }

    [Fact]
    public void ExtractPhones_CaseInsensitivePropertyAndParams()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\ntel;type=cell:+79990000000\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().ContainSingle().Which.Should().Be("79990000000");
    }

    [Fact]
    public void ExtractPhones_MultipleUntypedTel_KeepsAppearanceOrder()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL:+79991111111\nTEL:+79992222222\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().Equal("79991111111", "79992222222");
    }

    [Fact]
    public void ExtractPhones_GarbageTelValue_IsSkipped()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL:not-a-number\nTEL:+79990000000\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().ContainSingle().Which.Should().Be("79990000000");
    }

    [Fact]
    public void ExtractPhones_NoTelProperty_ReturnsEmpty()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nFN:Ivan Ivanov\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractPhones_EmptyInput_ReturnsEmpty(string? input) =>
        MaxVCardParser.ExtractPhones(input, DefaultMaxBytes).Should().BeEmpty();

    [Fact]
    public void ExtractPhones_BodyOverByteLimit_ReturnsEmptyWithoutParsing()
    {
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL:+79990000000\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, maxBytes: 5).Should().BeEmpty();
    }

    [Fact]
    public void ExtractPhones_TooManyTelProperties_DiscardsWholeCard()
    {
        var lines = Enumerable.Range(0, MaxVCardParser.MaxTelProperties + 1).Select(i => $"TEL:+7999000{i:0000}");
        var vcard = "BEGIN:VCARD\nVERSION:3.0\n" + string.Join('\n', lines) + "\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().BeEmpty();
    }

    [Fact]
    public void ExtractPhones_FoldedLine_IsUnfoldedBeforeParsing()
    {
        // RFC 6350 line folding: a continuation line starts with exactly one space.
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL\n :+79990000000\nEND:VCARD";
        MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes).Should().ContainSingle().Which.Should().Be("79990000000");
    }

    [Fact]
    public void ExtractPhones_MatchAmongAllNumbers_NotJustTheFirst()
    {
        // §147.3 p.4: a session's number can match ANY of the returned numbers, not only the first one.
        var vcard = "BEGIN:VCARD\nVERSION:3.0\nTEL;TYPE=CELL:+79991111111\nTEL:+79992222222\nEND:VCARD";
        var phones = MaxVCardParser.ExtractPhones(vcard, DefaultMaxBytes);
        phones.Should().Contain("79992222222");
    }
}
