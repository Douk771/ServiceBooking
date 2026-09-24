using QRCoder;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// Thin, pure wrapper over QRCoder (ARCHITECTURE_CYCLE14.md §141, §144.3) — encodes a deep link into a
/// PNG and returns it base64-encoded, with no <c>data:</c> prefix (the frontend prepends that itself,
/// §163). Only <see cref="PngByteQRCode"/> is used: no <c>System.Drawing</c>, no <c>SkiaSharp</c>, so the
/// choice never touches the app's own image pipeline or its Docker base image.
/// </summary>
public static class QrImage
{
    /// <summary>ECC level M — QRCoder's own recommended default, a reasonable balance between density
    /// (this payload is short) and resilience to a camera photographing a screen off-angle (R8's own
    /// "чужой, открывший ссылку первым" scenario starts with someone photographing this very image).</summary>
    private const QRCodeGenerator.ECCLevel EccLevel = QRCodeGenerator.ECCLevel.M;

    /// <summary>
    /// Encodes <paramref name="deepLink"/> as a QR code PNG.
    /// </summary>
    /// <returns>Raw PNG bytes — <see cref="Convert.ToBase64String(byte[])"/> before it goes into
    /// <c>PhoneVerificationSessionCreated.qrPngBase64</c>. Null is a valid contract value for that field
    /// (§141's documented fallback) but THIS method never returns null itself — a caller that wants the
    /// "no QR" fallback catches the exception and substitutes null, it isn't this function's job to hide
    /// a QRCoder failure as a silent null.</returns>
    public static byte[] EncodePng(string deepLink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLink);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(deepLink, EccLevel);
        var pngQrCode = new PngByteQRCode(data);
        return pngQrCode.GetGraphic(pixelsPerModule: 8);
    }
}
