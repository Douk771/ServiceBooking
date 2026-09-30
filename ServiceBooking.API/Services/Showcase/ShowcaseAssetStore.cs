using System.Security.Cryptography;
using System.Text.Json;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>One entry of <c>ShowcaseAssets/manifest.json</c> (§575.6). <see cref="Role"/> is <c>logo</c>, <c>photo</c> or <c>service</c>.</summary>
public sealed record ShowcaseAsset(string Key, string Role, string File, string? Thumbnail, int Width, int Height);

/// <summary>A showcase asset copied into <c>uploads/showcase/</c>: what the rows need (url, thumbnail, size, hash).</summary>
public sealed record PublishedShowcaseAsset(string Url, string? ThumbnailUrl, string ContentType, long SizeBytes, int Width, int Height, string ContentHash);

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.6 (A3, L28-4) — the ready-made pictures of the showcase live in the repository (<c>ServiceBooking.API/ShowcaseAssets/</c>, a
/// manifest and a <c>LICENSES.md</c> with the source of every file, no people on the pictures) and are copied, unchanged, into the separate public area
/// <c>uploads/showcase/</c> under content-addressed names, so copying the same file twice is a no-op. Nothing is processed or drawn: an asset that is not in the
/// manifest (or whose file is missing) is skipped — no placeholder, no row pointing at a file that does not exist.
/// </summary>
public sealed class ShowcaseAssetStore(FileStorage storage, IWebHostEnvironment env, ILogger<ShowcaseAssetStore> logger)
{
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, PublishedShowcaseAsset?> _published = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, ShowcaseAsset>? _manifest;

    public string AssetsDirectory => Path.Combine(env.ContentRootPath, "ShowcaseAssets");

    /// <summary>The manifest by key. Empty when there is no manifest file yet (the pictures are content task CT-1).</summary>
    public IReadOnlyDictionary<string, ShowcaseAsset> Manifest => _manifest ??= LoadManifest();

    /// <summary>Whether the asset is in the manifest AND its file exists on disk.</summary>
    public bool IsAvailable(string key) => Manifest.TryGetValue(key, out var asset) && ResolveFile(asset.File) is { } path && File.Exists(path);

    /// <summary>How many files the given keys would put into <c>uploads/showcase/</c> (a picture and its thumbnail count separately, distinct files once).</summary>
    public int CountFiles(IEnumerable<string> keys)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys.Distinct())
        {
            if (!IsAvailable(key)) continue;
            var asset = Manifest[key];
            files.Add(asset.File);
            if (asset.Thumbnail is not null && ResolveFile(asset.Thumbnail) is { } thumb && File.Exists(thumb)) files.Add(asset.Thumbnail);
        }
        return files.Count;
    }

    /// <summary>Copies the asset (once per store instance) and returns what a row needs, or null when the asset is not available.</summary>
    public async Task<PublishedShowcaseAsset?> PublishAsync(string key, CancellationToken ct)
    {
        if (_published.TryGetValue(key, out var done)) return done;
        PublishedShowcaseAsset? result = null;

        if (!Manifest.TryGetValue(key, out var asset))
        {
            logger.LogDebug("showcase asset {Key} is not in the manifest — skipped", key);
        }
        else if (ResolveFile(asset.File) is not { } path || !File.Exists(path))
        {
            logger.LogWarning("showcase asset {Key} is in the manifest but its file is missing — skipped", key);
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var url = await storage.SavePublicNamedAsync(PublicArea.Showcase, $"{hash[..32]}{extension}", bytes);

            string? thumbUrl = null;
            if (asset.Thumbnail is not null && ResolveFile(asset.Thumbnail) is { } thumbPath && File.Exists(thumbPath))
            {
                var thumbBytes = await File.ReadAllBytesAsync(thumbPath, ct);
                var thumbHash = Convert.ToHexString(SHA256.HashData(thumbBytes)).ToLowerInvariant();
                thumbUrl = await storage.SavePublicNamedAsync(PublicArea.Showcase, $"{thumbHash[..32]}{Path.GetExtension(thumbPath).ToLowerInvariant()}", thumbBytes);
            }

            result = new PublishedShowcaseAsset(url, thumbUrl, ContentTypeOf(extension), bytes.LongLength, asset.Width, asset.Height, hash);
        }

        _published[key] = result;
        return result;
    }

    private IReadOnlyDictionary<string, ShowcaseAsset> LoadManifest()
    {
        var path = Path.Combine(AssetsDirectory, ManifestFileName);
        if (!File.Exists(path)) return new Dictionary<string, ShowcaseAsset>();
        var manifest = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(path), ManifestJson);
        return (manifest?.Assets ?? []).ToDictionary(a => a.Key, a => a, StringComparer.Ordinal);
    }

    /// <summary>Resolves a manifest path inside the assets directory; anything that would leave it is refused (null).</summary>
    private string? ResolveFile(string relative)
    {
        var root = Path.GetFullPath(AssetsDirectory);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    private static string ContentTypeOf(string extension) => extension switch
    {
        ".webp" => "image/webp",
        ".png" => "image/png",
        _ => "image/jpeg",
    };

    private sealed record ManifestFile(int Version, List<ShowcaseAsset>? Assets);
}
