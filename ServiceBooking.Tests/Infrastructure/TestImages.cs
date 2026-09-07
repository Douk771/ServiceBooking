using SkiaSharp;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Produces small, genuinely decodable images for functional tests that exercise the real upload
/// pipeline (ImageSignature + ImageProcessor, US-19). Cycle A/pre-cycle-B tests used to post four bare
/// magic-number bytes (e.g. `0xFF 0xD8 0xFF 0xD9`) and rely on the server trusting Content-Type alone —
/// that upload path no longer exists (grep for IFormFile finds one path now), so any test exercising a
/// successful upload needs bytes an actual decoder accepts.
/// </summary>
public static class TestImages
{
    public static byte[] SolidJpeg(int width = 40, int height = 40, SKColor? color = null) =>
        Encode(width, height, color ?? SKColors.CornflowerBlue, SKEncodedImageFormat.Jpeg);

    public static byte[] SolidPng(int width = 40, int height = 40, SKColor? color = null) =>
        Encode(width, height, color ?? SKColors.CornflowerBlue, SKEncodedImageFormat.Png);

    public static byte[] SolidWebp(int width = 40, int height = 40, SKColor? color = null) =>
        Encode(width, height, color ?? SKColors.CornflowerBlue, SKEncodedImageFormat.Webp);

    /// <summary>A tall rectangle (not square) — useful for asserting a profile actually resizes/crops
    /// rather than merely accepting the file.</summary>
    public static byte[] TallJpeg(int width = 100, int height = 300) =>
        Encode(width, height, SKColors.MediumSeaGreen, SKEncodedImageFormat.Jpeg);

    private static byte[] Encode(int width, int height, SKColor color, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
