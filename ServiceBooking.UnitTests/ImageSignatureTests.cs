using FluentAssertions;
using ServiceBooking.API.Services;
using SkiaSharp;

namespace ServiceBooking.UnitTests;

public class ImageSignatureTests
{
    private static byte[] EncodeSolidBitmap(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public void TryDetect_RealJpegBytes_DetectedAsJpeg()
    {
        var bytes = EncodeSolidBitmap(20, 20, SKEncodedImageFormat.Jpeg);

        var detected = ImageSignature.TryDetect(bytes, out var kind);

        detected.Should().BeTrue();
        kind.Should().Be(ImageKind.Jpeg);
    }

    [Fact]
    public void TryDetect_RealPngBytes_DetectedAsPng()
    {
        var bytes = EncodeSolidBitmap(20, 20, SKEncodedImageFormat.Png);

        var detected = ImageSignature.TryDetect(bytes, out var kind);

        detected.Should().BeTrue();
        kind.Should().Be(ImageKind.Png);
    }

    [Fact]
    public void TryDetect_RealWebpBytes_DetectedAsWebp()
    {
        var bytes = EncodeSolidBitmap(20, 20, SKEncodedImageFormat.Webp);

        var detected = ImageSignature.TryDetect(bytes, out var kind);

        detected.Should().BeTrue();
        kind.Should().Be(ImageKind.Webp);
    }

    [Fact]
    public void TryDetect_HandCraftedRiffWebpHeader_DetectedAsWebp()
    {
        // "RIFF" + 4-byte size (irrelevant to detection) + "WEBP" + arbitrary chunk bytes.
        byte[] bytes = [.. "RIFF"u8.ToArray(), 0x00, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray(), 0x01, 0x02];

        ImageSignature.TryDetect(bytes, out var kind).Should().BeTrue();
        kind.Should().Be(ImageKind.Webp);
    }

    [Fact]
    public void TryDetect_PlainTextRenamedAsJpeg_IsNotRecognised()
    {
        // The ".jpg with a text file inside" attack: content-type/extension are never consulted here,
        // only the actual bytes — and these bytes are not a JPEG signature.
        var bytes = "this is definitely not an image, just text pretending to be one"u8.ToArray();

        var detected = ImageSignature.TryDetect(bytes, out _);

        detected.Should().BeFalse();
    }

    [Fact]
    public void TryDetect_EmptyInput_IsNotRecognised()
    {
        ImageSignature.TryDetect(ReadOnlySpan<byte>.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDetect_TruncatedBelowMagicLength_IsNotRecognised()
    {
        byte[] bytes = [0xFF, 0xD8]; // one byte short of the 3-byte JPEG magic

        ImageSignature.TryDetect(bytes, out _).Should().BeFalse();
    }
}
