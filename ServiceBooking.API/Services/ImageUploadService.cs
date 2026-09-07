namespace ServiceBooking.API.Services;

/// <summary>Outcome of the shared, entity-agnostic half of the upload pipeline (ARCHITECTURE.md §4.1,
/// steps 3-5, 7-8): file presence, size limit, signature check, disk space, decode/resize/re-encode.
/// Steps 6 (permission on the target entity), 9 (quota, private class only), 10-11 (write + persist) are
/// entity-specific and stay in the calling controller — this type only carries the two things every
/// caller needs next: whether it succeeded, and either the processed image(s) or the message to
/// return.</summary>
public sealed class UploadValidationResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    private readonly IReadOnlyList<ProcessedImage>? _images;

    /// <summary>The single processed image — for the three public upload endpoints, which each produce
    /// exactly one output. Client-note photos use <see cref="Images"/> instead (main + thumbnail).</summary>
    public ProcessedImage? Image => _images?[0];

    /// <summary>All processed outputs, in the order their profiles were requested.</summary>
    public IReadOnlyList<ProcessedImage>? Images => _images;

    private UploadValidationResult(bool success, string? errorMessage, IReadOnlyList<ProcessedImage>? images)
    {
        Success = success;
        ErrorMessage = errorMessage;
        _images = images;
    }

    public static UploadValidationResult Ok(IReadOnlyList<ProcessedImage> images) => new(true, null, images);
    public static UploadValidationResult Fail(string message) => new(false, message, null);
}

/// <summary>
/// Orchestrates the entity-agnostic half of every image upload in the product (ARCHITECTURE.md §4.1):
/// validate presence and size, detect the real format by signature (never Content-Type/filename — US-19
/// p.1), check disk space, then decode/orient/resize/re-encode via <see cref="ImageProcessor"/>. All four
/// upload endpoints (client-note photo, avatar, service image, company logo) call this exact same code —
/// grep for `IFormFile` in the controllers finds four call sites and one code path behind them
/// (ARCHITECTURE.md §21, US-25 p.2/p.6).
///
/// What this class deliberately does NOT do: authorization (the target entity differs per endpoint and
/// stays a private predicate in the controller, per project convention), quota accounting (only the
/// private class has one, and it needs a transaction + advisory lock the caller already holds for other
/// reasons), and persistence (writing the file and the DB row is the last two pipeline steps, and their
/// exact shape — one file vs. two, a new row vs. an entity update — differs per caller).
/// </summary>
public class ImageUploadService(FileStorage storage, IConfiguration config, ILogger<ImageUploadService> logger)
{
    private readonly long _maxFileBytes = config.GetValue("Uploads:MaxFileBytes", 5 * 1024 * 1024L);

    /// <summary>Validates and processes an upload into a single profile — the shape every public upload
    /// endpoint (avatar, service image, company logo) needs.</summary>
    public async Task<UploadValidationResult> ReadAndProcessAsync(IFormFile? file, ImageProfile profile) =>
        await ReadAndProcessAsync(file, [profile]);

    /// <summary>
    /// Validates once and produces one <see cref="ProcessedImage"/> per requested profile from the SAME
    /// decoded source — used for client-note photos, which need both a full-size (1600px) and a
    /// thumbnail (320px) rendition of one upload. Reading the file and checking its signature only once
    /// (rather than once per profile) is what keeps this a single pipeline pass rather than two.
    /// </summary>
    public async Task<UploadValidationResult> ReadAndProcessAsync(IFormFile? file, IReadOnlyList<ImageProfile> profiles)
    {
        if (file is null || file.Length == 0)
            return UploadValidationResult.Fail("File is required");

        if (file.Length > _maxFileBytes)
            return UploadValidationResult.Fail("Image is too large — the limit is 5 MB");

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }

        // Content-Type and the client-supplied filename are never consulted anywhere in this pipeline —
        // only the bytes decide (US-19 p.1). This is also what fixes the previous logo upload, which
        // trusted Content-Type alone (CompaniesController.cs:271-281 before this cycle).
        if (!ImageSignature.TryDetect(bytes, out var kind))
            return UploadValidationResult.Fail("Unsupported image type — use JPEG, PNG or WEBP");

        // Checked against the raw upload size (an upper bound on what any profile will end up writing —
        // every profile only shrinks the image) rather than the eventual processed size, which isn't
        // known until after the (comparatively expensive) decode/resize below.
        if (!storage.HasFreeSpace(bytes.LongLength))
        {
            logger.LogWarning("Upload rejected: server storage is below the configured free-space threshold");
            return UploadValidationResult.Fail("Server storage is full — try again later.");
        }

        try
        {
            var images = profiles.Select(p => ImageProcessor.Process(bytes, kind, p)).ToList();
            return UploadValidationResult.Ok(images);
        }
        catch (ImageTooLargeException)
        {
            // Deliberately mentions "too large" (matches the existing byte-size message's wording) so
            // the frontend's existing uploadError.ts bucket for that phrase — "Файл больше 5 МБ.
            // Уменьшите изображение и попробуйте снова." — applies here too, rather than falling through
            // to the generic catch-all. This check never even reaches SKBitmap.Decode (code review
            // finding, decompression-bomb risk) — see ImageProcessor.MaxPixels.
            return UploadValidationResult.Fail("Image dimensions are too large — try a smaller image.");
        }
        catch (InvalidImageException)
        {
            return UploadValidationResult.Fail("File is not a valid image");
        }
    }
}
