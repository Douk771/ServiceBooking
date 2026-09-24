using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE12.md §141, §156.1 — the QR encoder produces a real, decodable PNG.</summary>
public class QrImageTests
{
    [Fact]
    public void EncodePng_ProducesValidPngBytes()
    {
        var bytes = QrImage.EncodePng("https://max.ru/ezbookbot?start=v1.abcdefgh");

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        bytes.Should().NotBeEmpty();
        bytes.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    }

    [Fact]
    public void EncodePng_DifferentInputs_ProduceDifferentImages()
    {
        var a = QrImage.EncodePng("https://max.ru/bot?start=payload-a");
        var b = QrImage.EncodePng("https://max.ru/bot?start=payload-b");
        a.Should().NotEqual(b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EncodePng_EmptyInput_Throws(string? deepLink)
    {
        var act = () => QrImage.EncodePng(deepLink!);
        act.Should().Throw<ArgumentException>();
    }
}
