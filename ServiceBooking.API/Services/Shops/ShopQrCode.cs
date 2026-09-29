using QRCoder;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §390 — the QR code of a shop's link for printing: PNG, error correction level Q (a printed code
/// gets scuffed), about 2000×2000 px so an A4 print at ~240 dpi stays sharp. The same QRCoder package cycle 14 added.
/// </summary>
public static class ShopQrCode
{
    public const int TargetPixels = 2000;

    public static byte[] EncodePng(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var modules = data.ModuleMatrix.Count; // includes the quiet zone (QRCoder adds it to the matrix)
        var pixelsPerModule = Math.Max(1, TargetPixels / modules);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
