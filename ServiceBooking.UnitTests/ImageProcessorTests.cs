using System.Text;
using FluentAssertions;
using ServiceBooking.API.Services;
using SkiaSharp;

namespace ServiceBooking.UnitTests;

public class ImageProcessorTests
{
    private static byte[] EncodeSolidBitmap(int width, int height, SKColor color, SKEncodedImageFormat format, int quality = 90)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    // ── Resize ───────────────────────────────────────────────────────────────

    [Fact]
    public void Process_LargeLandscapeJpeg_ResizesLongSideToProfileMaximum()
    {
        var source = EncodeSolidBitmap(3000, 2000, SKColors.CornflowerBlue, SKEncodedImageFormat.Jpeg);

        var result = ImageProcessor.Process(source, ImageKind.Jpeg, ImageProfile.ClientNotePhoto);

        // ARCHITECTURE.md T-B2: 3000x2000 -> 1600x1067 (aspect preserved, longer side capped at 1600).
        result.Width.Should().Be(1600);
        result.Height.Should().Be(1067);
    }

    [Fact]
    public void Process_ImageSmallerThanProfileMaximum_IsNotUpscaled()
    {
        var source = EncodeSolidBitmap(800, 600, SKColors.CornflowerBlue, SKEncodedImageFormat.Jpeg);

        var result = ImageProcessor.Process(source, ImageKind.Jpeg, ImageProfile.ClientNotePhoto);

        result.Width.Should().Be(800);
        result.Height.Should().Be(600);
    }

    // ── Output format rule (ARCHITECTURE.md §4.4) ───────────────────────────

    [Fact]
    public void Process_ClientNotePhotoProfile_AlwaysEncodesJpeg_EvenForPngSource()
    {
        var source = EncodeSolidBitmap(100, 100, SKColors.Red, SKEncodedImageFormat.Png);

        var result = ImageProcessor.Process(source, ImageKind.Png, ImageProfile.ClientNotePhoto);

        result.ContentType.Should().Be("image/jpeg");
        result.Extension.Should().Be(".jpg");
    }

    [Fact]
    public void Process_PublicProfile_PngSource_StaysPng()
    {
        var source = EncodeSolidBitmap(100, 100, SKColors.Red, SKEncodedImageFormat.Png);

        var result = ImageProcessor.Process(source, ImageKind.Png, ImageProfile.CompanyLogo);

        result.ContentType.Should().Be("image/png");
        result.Extension.Should().Be(".png");
    }

    [Fact]
    public void Process_PublicProfile_JpegSource_StaysJpeg()
    {
        var source = EncodeSolidBitmap(100, 100, SKColors.Red, SKEncodedImageFormat.Jpeg);

        var result = ImageProcessor.Process(source, ImageKind.Jpeg, ImageProfile.CompanyLogo);

        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public void Process_PublicProfile_WebpSource_BecomesJpeg()
    {
        var source = EncodeSolidBitmap(100, 100, SKColors.Red, SKEncodedImageFormat.Webp);

        var result = ImageProcessor.Process(source, ImageKind.Webp, ImageProfile.CompanyLogo);

        result.ContentType.Should().Be("image/jpeg");
    }

    // ── Square crop (avatar) ─────────────────────────────────────────────────

    [Fact]
    public void Process_AvatarProfile_CropsToSquareAndCapsDimension()
    {
        var source = EncodeSolidBitmap(2000, 1000, SKColors.Red, SKEncodedImageFormat.Jpeg);

        var result = ImageProcessor.Process(source, ImageKind.Jpeg, ImageProfile.Avatar);

        result.Width.Should().Be(512);
        result.Height.Should().Be(512);
    }

    // ── EXIF is gone from the output (US-19 p.3 / MC-2xv) ────────────────────

    [Fact]
    public void Process_SourceWithFakeExifSegment_OutputContainsNoExifMarker()
    {
        var plainJpeg = EncodeSolidBitmap(50, 50, SKColors.Green, SKEncodedImageFormat.Jpeg);
        var withExif = InsertFakeExifApp1Segment(plainJpeg);

        // Sanity check the input actually carries the marker we're about to prove disappears.
        Encoding.ASCII.GetString(withExif).Should().Contain("Exif");

        var result = ImageProcessor.Process(withExif, ImageKind.Jpeg, ImageProfile.ClientNotePhoto);

        // Re-encoding from a plain pixel bitmap writes only pixels — Skia's encoders don't carry
        // metadata across unless explicitly asked to, so there is no "strip EXIF" step: there is
        // simply nothing to strip in the output.
        Encoding.ASCII.GetString(result.Bytes).Should().NotContain("Exif");
    }

    /// <summary>Splices a fake APP1 "Exif" segment right after a real JPEG's SOI marker, so the file
    /// still decodes normally but the raw bytes contain the "Exif" ASCII marker somewhere in the file —
    /// exactly the shape of metadata this pipeline is required to discard.</summary>
    private static byte[] InsertFakeExifApp1Segment(byte[] jpeg)
    {
        byte[] app1Data = [.. "Exif\0\0"u8.ToArray(), 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00];
        var segmentLength = app1Data.Length + 2; // length field includes itself, excludes the marker
        byte[] app1 = [0xFF, 0xE1, (byte)(segmentLength >> 8), (byte)(segmentLength & 0xFF), .. app1Data];

        // jpeg[0..1] is the SOI marker (0xFFD8); everything after it is untouched.
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    // ── Orientation (ARCHITECTURE.md §4.3) ──────────────────────────────────

    [Fact]
    public void ApplyOrigin_TopLeft_ReturnsSameInstanceUnchanged()
    {
        using var bitmap = new SKBitmap(100, 200);

        var result = ImageProcessor.ApplyOrigin(bitmap, SKEncodedOrigin.TopLeft);

        result.Should().BeSameAs(bitmap);
    }

    [Fact]
    public void ApplyOrigin_RightTop_SwapsDimensionsAndRotatesPixelsNinetyDegreesClockwise()
    {
        // A 100x200 bitmap with a solid red 10x10 marker block in its top-left corner, blue elsewhere.
        using var source = new SKBitmap(100, 200);
        source.Erase(SKColors.Blue);
        for (var x = 0; x < 10; x++)
            for (var y = 0; y < 10; y++)
                source.SetPixel(x, y, SKColors.Red);

        using var oriented = ImageProcessor.ApplyOrigin(source, SKEncodedOrigin.RightTop);

        // ARCHITECTURE.md §4.2 T-B2: "100x200 with Orientation=6 -> 200x100 after processing".
        oriented.Width.Should().Be(200);
        oriented.Height.Should().Be(100);

        // A 90-degree clockwise rotation moves the source's top-left corner to the result's top-right
        // corner (verified analytically against SKCanvas's transform order — see the code comment on
        // ApplyOrigin's RightTop case).
        oriented.GetPixel(195, 5).Should().Be(SKColors.Red);
        oriented.GetPixel(5, 5).Should().Be(SKColors.Blue);
        oriented.GetPixel(5, 95).Should().Be(SKColors.Blue);
        oriented.GetPixel(195, 95).Should().Be(SKColors.Blue);
    }

    // ── Center crop ──────────────────────────────────────────────────────────

    [Fact]
    public void CenterCropToSquare_WideBitmap_CropsToSmallerSideCenteredHorizontally()
    {
        using var source = new SKBitmap(200, 100);

        using var cropped = ImageProcessor.CenterCropToSquare(source);

        cropped.Width.Should().Be(100);
        cropped.Height.Should().Be(100);
    }

    [Fact]
    public void CenterCropToSquare_AlreadySquare_ReturnsSameInstance()
    {
        using var source = new SKBitmap(150, 150);

        var cropped = ImageProcessor.CenterCropToSquare(source);

        cropped.Should().BeSameAs(source);
    }

    // ── Resize helper boundary ───────────────────────────────────────────────

    [Fact]
    public void ResizeToFit_ExactlyAtMaximum_ReturnsSameInstance()
    {
        using var source = new SKBitmap(1600, 900);

        var resized = ImageProcessor.ResizeToFit(source, 1600);

        resized.Should().BeSameAs(source);
    }

    // ── Decompression-bomb guard (code review finding, round 3) ─────────────

    [Fact]
    public void Process_PngDeclaringDimensionsAboveThePixelBudget_ThrowsBeforeDecoding()
    {
        // A genuine tiny (1x1) PNG whose IHDR header is then patched to declare 20000x20000 (400M
        // pixels) — the exact "small file, huge declared dimensions" shape a decompression bomb takes.
        // This proves the guard reads codec.Info from the header rather than needing SKBitmap.Decode to
        // ever run (which would otherwise try to allocate ~1.6 GB for real).
        var tinyPng = EncodeSolidBitmap(1, 1, SKColors.Red, SKEncodedImageFormat.Png);
        var bomb = PatchPngDeclaredDimensions(tinyPng, 20000, 20000);

        var act = () => ImageProcessor.Process(bomb, ImageKind.Png, ImageProfile.Avatar);

        act.Should().Throw<ImageTooLargeException>();
    }

    [Fact]
    public void Process_PngDeclaringDimensionsExactlyAtThePixelBudget_IsNotRejectedByTheGuard()
    {
        // 10000x5000 = 50,000,000 == MaxPixels exactly — the guard only rejects strictly above it.
        var tinyPng = EncodeSolidBitmap(1, 1, SKColors.Red, SKEncodedImageFormat.Png);
        var atBudget = PatchPngDeclaredDimensions(tinyPng, 10000, 5000);

        // The declared header (10000x5000) lies about the real 1x1 pixel data, so decoding fails once
        // the guard lets it through — proving this is specifically an ImageTooLargeException test, not
        // one that happens to throw InvalidImageException for an unrelated reason (the guard itself is
        // exercised, decoding the (deliberately mismatched) rest of the file is not this test's concern).
        var act = () => ImageProcessor.Process(atBudget, ImageKind.Png, ImageProfile.Avatar);

        act.Should().NotThrow<ImageTooLargeException>();
    }

    /// <summary>Rewrites a real PNG's IHDR width/height fields (and recomputes the IHDR chunk's CRC32) to
    /// declare different dimensions than the actual pixel data — without needing to materialize a real
    /// bitmap anywhere near the declared size, keeping this test fast and memory-light.</summary>
    private static byte[] PatchPngDeclaredDimensions(byte[] png, uint width, uint height)
    {
        var bytes = (byte[])png.Clone();

        WriteUInt32BigEndian(bytes, 16, width);
        WriteUInt32BigEndian(bytes, 20, height);

        // The IHDR chunk's CRC32 covers its 4-byte type ("IHDR") plus its 13 bytes of data (offsets
        // 12..29) — Skia validates this, so it has to be recomputed after patching the dimensions above.
        var crc = Crc32(bytes.AsSpan(12, 17));
        WriteUInt32BigEndian(bytes, 29, crc);

        return bytes;
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>Standard PNG/zlib CRC-32 (polynomial 0xEDB88320) — .NET has no built-in implementation
    /// available to this project, and pulling in a package for one test fixture wasn't worth it.</summary>
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}
