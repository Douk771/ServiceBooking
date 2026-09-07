using SkiaSharp;

namespace ServiceBooking.API.Services;

/// <summary>Thrown when the bytes pass signature detection (<see cref="ImageSignature"/>) but the Skia
/// decoder cannot make a bitmap out of them — a corrupt or maliciously-crafted file (US-19 pipeline
/// step 8, ARCHITECTURE.md §4.1).</summary>
public sealed class InvalidImageException() : Exception("File is not a valid image");

/// <summary>
/// Thrown when the ENCODED image's own declared pixel dimensions exceed <see cref="ImageProcessor.MaxPixels"/>
/// — checked before <c>SKBitmap.Decode</c> is ever called (code review finding, decompression-bomb risk:
/// a file well under the 5 MB byte-size limit can still declare dimensions like 20000×20000, which
/// SKBitmap.Decode would try to materialize as an uncompressed ~1.6 GB bitmap in memory — twice over for
/// client-note photos, which decode once per profile, full-size and thumbnail). Reachable by ANY
/// authenticated user through the least-privileged upload endpoint (POST /api/profile/avatar), not just
/// staff, so this has to be cheap and unconditional, not a company-scoped guard.
/// </summary>
public sealed class ImageTooLargeException()
    : Exception($"Image dimensions are too large — the limit is {ImageProcessor.MaxPixels / 1_000_000} megapixels");

/// <summary>Whether the output format follows the input, or is fixed regardless of it — see
/// ARCHITECTURE.md §4.4: private client-note photos are always re-encoded to JPEG; public images keep
/// PNG (for its alpha channel) when the input was PNG, and become JPEG otherwise.</summary>
public enum OutputFormatPolicy { AlwaysJpeg, PngIfSourceWasPngElseJpeg }

/// <summary>One named processing recipe (ARCHITECTURE.md §4.2). Deliberately data, not behaviour, so
/// every upload endpoint shares the exact same <see cref="ImageProcessor.Process"/> code path.</summary>
public sealed record ImageProfile(int MaxDimension, bool SquareCrop, OutputFormatPolicy FormatPolicy, int JpegQuality)
{
    public static readonly ImageProfile ClientNotePhoto = new(1600, SquareCrop: false, OutputFormatPolicy.AlwaysJpeg, JpegQuality: 82);
    public static readonly ImageProfile ClientNotePhotoThumb = new(320, SquareCrop: false, OutputFormatPolicy.AlwaysJpeg, JpegQuality: 75);
    public static readonly ImageProfile Avatar = new(512, SquareCrop: true, OutputFormatPolicy.PngIfSourceWasPngElseJpeg, JpegQuality: 85);
    public static readonly ImageProfile ServiceImage = new(1200, SquareCrop: false, OutputFormatPolicy.PngIfSourceWasPngElseJpeg, JpegQuality: 85);
    public static readonly ImageProfile CompanyLogo = new(512, SquareCrop: false, OutputFormatPolicy.PngIfSourceWasPngElseJpeg, JpegQuality: 85);
}

public sealed record ProcessedImage(byte[] Bytes, int Width, int Height, string ContentType, string Extension);

/// <summary>
/// Decodes, re-orients, resizes, (optionally) center-crops and re-encodes an image — with no disk, no
/// EF, no HTTP, so it is fully unit-testable (ARCHITECTURE.md §3.2). Re-encoding is what strips EXIF:
/// Skia's encoders write only pixels, so geotags and device metadata simply don't exist in the output
/// (US-19 p.3) — there is no separate "remove metadata" step.
/// </summary>
public static class ImageProcessor
{
    /// <summary>Hard ceiling on ENCODED pixel count (width × height), checked before decoding — 50
    /// megapixels is generously above anything a real camera/phone photo needs (a 1600px-long-side photo
    /// is ~2-3 MP after this pipeline resizes it) while still rejecting the "small file, huge declared
    /// dimensions" decompression-bomb shape (code review finding).</summary>
    public const long MaxPixels = 50_000_000;

    /// <param name="source">The raw file bytes, already confirmed by <see cref="ImageSignature"/> to be
    /// one of the three supported kinds.</param>
    /// <param name="sourceKind">The kind <see cref="ImageSignature"/> already detected — passed in rather
    /// than re-detected, so the two checks (signature, decode) stay two independent steps of the upload
    /// pipeline instead of silently duplicating work.</param>
    /// <param name="profile">The processing recipe to apply — see <see cref="ImageProfile"/>.</param>
    public static ProcessedImage Process(byte[] source, ImageKind sourceKind, ImageProfile profile)
    {
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data) ?? throw new InvalidImageException();

        // Checked from the codec's own declared header info — BEFORE SKBitmap.Decode ever allocates a
        // pixel buffer. A file can be well under the 5 MB byte-size limit and still declare enormous
        // dimensions; decoding would materialize width × height × 4 bytes uncompressed regardless of how
        // small the encoded file was (risk: decompression bomb, reachable by any authenticated user via
        // POST /api/profile/avatar, doubled for client-note photos which decode full-size + thumbnail).
        var declaredPixels = (long)codec.Info.Width * codec.Info.Height;
        if (declaredPixels > MaxPixels) throw new ImageTooLargeException();

        // The one piece of metadata we read on purpose, before everything else is discarded (§4.3):
        // without applying it to the pixels, every portrait photo from a phone would come out rotated.
        var origin = codec.EncodedOrigin;

        var decoded = SKBitmap.Decode(codec) ?? throw new InvalidImageException();
        // `decoded` is deliberately NOT wrapped in its own `using`: ApplyOrigin returns the very same
        // instance when origin is TopLeft (the common case), and disposing it separately here as well as
        // via `current` below would double-dispose one object (code review finding — harmless in
        // SkiaSharp today, since SKObject.Dispose() is idempotent, but not something to structurally rely
        // on). From this point on, `current` is the single tracked owner of whichever bitmap is "live" at
        // each step, and the `finally` below is the only place anything gets disposed.
        var current = ApplyOrigin(decoded, origin);
        try
        {
            if (profile.SquareCrop)
                current = ReplaceWith(current, CenterCropToSquare(current));

            current = ReplaceWith(current, ResizeToFit(current, profile.MaxDimension));

            var useJpeg = profile.FormatPolicy == OutputFormatPolicy.AlwaysJpeg || sourceKind != ImageKind.Png;
            var format = useJpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
            // Skia ignores the quality parameter for PNG (lossless), so any value is fine there.
            var quality = useJpeg ? profile.JpegQuality : 100;

            using var image = SKImage.FromBitmap(current);
            using var encoded = image.Encode(format, quality) ?? throw new InvalidImageException();

            return new ProcessedImage(
                encoded.ToArray(), current.Width, current.Height,
                useJpeg ? "image/jpeg" : "image/png", useJpeg ? ".jpg" : ".png");
        }
        finally
        {
            current.Dispose();
        }
    }

    /// <summary>Disposes <paramref name="previous"/> when <paramref name="next"/> is a different
    /// instance (a transformation ran), and returns <paramref name="next"/> — keeps every step below a
    /// single ownership chain without leaking an intermediate bitmap.</summary>
    private static SKBitmap ReplaceWith(SKBitmap previous, SKBitmap next)
    {
        if (!ReferenceEquals(previous, next)) previous.Dispose();
        return next;
    }

    /// <summary>
    /// Applies the EXIF orientation baked into the file to the actual pixels, so the output needs no
    /// orientation metadata at all — see the class doc and ARCHITECTURE.md §4.3. Internal (not private)
    /// so ImageProcessorTests can drive it directly with a synthetic bitmap + known
    /// <see cref="SKEncodedOrigin"/>, instead of hand-crafting a JPEG with a real EXIF APP1 segment just
    /// to exercise this — the decoder's own EXIF parsing is Skia's problem, not ours to re-test.
    /// </summary>
    internal static SKBitmap ApplyOrigin(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return bitmap;

        var swapsDimensions = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapsDimensions ? bitmap.Height : bitmap.Width;
        var height = swapsDimensions ? bitmap.Width : bitmap.Height;

        var result = new SKBitmap(width, height, bitmap.ColorType, bitmap.AlphaType);
        using (var canvas = new SKCanvas(result))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: // mirrored horizontally
                    canvas.Translate(width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight: // rotated 180
                    canvas.Translate(width, height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft: // mirrored vertically
                    canvas.Translate(0, height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.LeftTop: // transposed
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop: // rotated 90° clockwise — the common phone-portrait case
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom: // transversed
                    canvas.Translate(width, height);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.LeftBottom: // rotated 90° counter-clockwise
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(-90);
                    break;
            }

            canvas.DrawBitmap(bitmap, 0, 0);
        }

        return result;
    }

    /// <summary>Crops the largest centered square out of the bitmap — used for the avatar profile.</summary>
    internal static SKBitmap CenterCropToSquare(SKBitmap bitmap)
    {
        var side = Math.Min(bitmap.Width, bitmap.Height);
        if (side == bitmap.Width && side == bitmap.Height) return bitmap;

        var left = (bitmap.Width - side) / 2;
        var top = (bitmap.Height - side) / 2;
        var result = new SKBitmap(side, side, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(bitmap, SKRect.Create(left, top, side, side), SKRect.Create(0, 0, side, side));
        return result;
    }

    /// <summary>
    /// Scales down so the longer side is at most <paramref name="maxDimension"/>, preserving aspect
    /// ratio. Never upscales (ARCHITECTURE.md §4.2): an image already smaller than the target is
    /// returned unchanged (it still gets re-encoded by the caller, which is what strips EXIF).
    /// </summary>
    internal static SKBitmap ResizeToFit(SKBitmap bitmap, int maxDimension)
    {
        var longSide = Math.Max(bitmap.Width, bitmap.Height);
        if (longSide <= maxDimension) return bitmap;

        var scale = (double)maxDimension / longSide;
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        var resized = bitmap.Resize(new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType), SKFilterQuality.High);
        return resized ?? throw new InvalidImageException();
    }
}
