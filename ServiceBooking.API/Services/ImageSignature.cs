namespace ServiceBooking.API.Services;

public enum ImageKind { Jpeg, Png, Webp }

/// <summary>
/// Detects an image's real format from its first bytes (magic numbers), never from the client-supplied
/// Content-Type or filename — those are attacker-controlled and are not read anywhere in the upload
/// pipeline (US-19 p.1). Pure and dependency-free: no disk, no HTTP, no EF — unit-tested directly.
/// </summary>
public static class ImageSignature
{
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Recognises JPEG, PNG and WEBP by their leading bytes. Returns false for anything else, including
    /// a file whose extension/Content-Type claims to be one of these three but whose content isn't
    /// (the ".jpg with a text file inside" attack this exists to stop).
    /// </summary>
    public static bool TryDetect(ReadOnlySpan<byte> bytes, out ImageKind kind)
    {
        if (bytes.Length >= JpegMagic.Length && bytes[..JpegMagic.Length].SequenceEqual(JpegMagic))
        {
            kind = ImageKind.Jpeg;
            return true;
        }

        if (bytes.Length >= PngMagic.Length && bytes[..PngMagic.Length].SequenceEqual(PngMagic))
        {
            kind = ImageKind.Png;
            return true;
        }

        // WEBP is a RIFF container: bytes 0-3 "RIFF", bytes 4-7 the chunk size (ignored here), bytes
        // 8-11 the four-character code "WEBP".
        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            kind = ImageKind.Webp;
            return true;
        }

        kind = default;
        return false;
    }
}
