using Microsoft.Extensions.Configuration;

namespace ServiceBooking.API.Services;

/// <summary>Named subfolder of the public storage root — every public image class gets one, mirroring
/// what the existing company-logo upload already did (`wwwroot/uploads/companies/`).</summary>
public enum PublicArea { Avatars, Services, Companies }

/// <summary>
/// Owns where uploaded files live: every default path is resolved here, and DeploymentSafetyChecks
/// validates configuration through the same helpers rather than recomputing them (ARCHITECTURE.md §3.2).
/// It serves two
/// deliberately different storage classes:
///
/// WHY TWO CLASSES, NOT ONE CONFIGURABLE FOLDER: the public class (logo, avatar, service image) is
/// meant to be reachable by anyone with the URL — that is its whole point, a storefront needs it visible
/// to an anonymous browser via app.UseStaticFiles(). The private class (client-note photos) must be the
/// opposite: personal data of a salon's customers that only that salon's own staff may ever see. Coding
/// them as the same "upload a file, get a path back" helper with an `isPublic` flag would make it one
/// misplaced `if` away from leaking photos through the static file middleware. Splitting them into two
/// methods with two different return shapes — a URL you can put in an <c>&lt;img src&gt;</c> versus an
/// opaque storage key that isn't a URL at all — makes that mistake a compile error instead of a review
/// finding: nothing about a storage key even looks like something you could hand to the browser.
///
/// Db rows follow the same split (ARCHITECTURE.md §3.1): public classes store the URL, the private class
/// stores only the key, and there is no reachable path from a key back to a URL anywhere in this class.
/// </summary>
public class FileStorage
{
    private readonly string _publicRoot;
    private readonly string _privateRoot;
    private readonly long _minFreeDiskBytes;

    public FileStorage(IConfiguration config, IWebHostEnvironment env)
    {
        _publicRoot = ResolvePublicRoot(config, env.ContentRootPath);
        _privateRoot = ResolvePrivateRoot(config, env.ContentRootPath);

        var minFreeMb = config.GetValue("Storage:MinFreeDiskMb", 1024);
        _minFreeDiskBytes = minFreeMb * 1024L * 1024L;
    }

    /// <summary>
    /// Resolves Storage:PublicRoot the same way the constructor does, but callable before a
    /// <see cref="FileStorage"/> instance — or even the DI container — exists. Extracted (sanitation
    /// cycle, review round 2) so DeploymentSafetyChecks.ValidateSecrets, which runs before
    /// WebApplicationBuilder.Build(), computes the exact same default as the class that actually opens
    /// the files, instead of keeping a second, independently-maintained copy of this logic that could
    /// silently drift from this one.
    /// </summary>
    public static string ResolvePublicRoot(IConfiguration config, string contentRootPath) =>
        config["Storage:PublicRoot"] is { Length: > 0 } publicRoot
            ? publicRoot
            : Path.Combine(contentRootPath, "wwwroot", "uploads");

    /// <summary>Resolves Storage:PrivateRoot the same way the constructor does — see
    /// <see cref="ResolvePublicRoot"/> for why this is a public static helper rather than
    /// constructor-only logic. App_Data is the one folder name IIS refuses to serve over HTTP by
    /// default; on Linux it is just a directory name with no special meaning, chosen only for parity
    /// across both deploy contours (ARCHITECTURE.md §3.3).</summary>
    public static string ResolvePrivateRoot(IConfiguration config, string contentRootPath) =>
        config["Storage:PrivateRoot"] is { Length: > 0 } privateRoot
            ? privateRoot
            : Path.Combine(contentRootPath, "App_Data", "private-uploads");

    /// <summary>Absolute path of the private root, used once by Program.cs's Production fail-fast check
    /// (§3.4) — the only caller outside this class that needs the raw path rather than a key/URL.</summary>
    public string PrivateRootFullPath => Path.GetFullPath(_privateRoot);

    /// <summary>Absolute path of the public root, for the same fail-fast comparison.</summary>
    public string PublicRootFullPath => Path.GetFullPath(_publicRoot);

    /// <summary>Writes a public file and returns the URL <c>UseStaticFiles</c> will serve it at.</summary>
    public async Task<string> SavePublicAsync(PublicArea area, byte[] bytes, string extension)
    {
        var folder = AreaFolder(area);
        var dir = Path.Combine(_publicRoot, folder);
        Directory.CreateDirectory(dir);

        var fileName = $"{Guid.NewGuid()}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), bytes);
        return $"/uploads/{folder}/{fileName}";
    }

    /// <summary>Deletes a previously-saved public file by its URL. A no-op for null/empty, for a URL
    /// this class didn't produce (e.g. a legacy external logo URL), or for anything that would resolve
    /// outside the public root — mirrors <see cref="ResolvePrivatePath"/>'s containment check (code
    /// review finding: this method used to trust the "/uploads/" prefix alone and combine+delete without
    /// verifying containment, unlike the private counterpart. A caller that lets a client influence this
    /// URL at all — e.g. an entity field a client could set directly rather than only via the upload
    /// endpoints — could otherwise delete an arbitrary file the process has permission to remove via
    /// "/uploads/../../appsettings.Production.json"-style traversal).</summary>
    public void DeletePublic(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/", StringComparison.Ordinal)) return;

        var relative = url["/uploads/".Length..];
        var candidate = ResolveContained(PublicRootFullPath, relative);
        if (candidate is not null && File.Exists(candidate)) File.Delete(candidate);
    }

    /// <summary>Writes a private file under the company's own subfolder and returns an opaque storage
    /// key ("&lt;companyId&gt;/&lt;guid&gt;.ext") — never a URL, never usable directly by a browser.</summary>
    public async Task<string> SavePrivateAsync(Guid companyId, byte[] bytes, string extension)
    {
        var dir = Path.Combine(_privateRoot, companyId.ToString());
        Directory.CreateDirectory(dir);

        var fileName = $"{Guid.NewGuid()}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), bytes);
        return $"{companyId}/{fileName}";
    }

    /// <summary>
    /// Opens a private file for reading by its storage key. No <c>Async</c> suffix (code review finding):
    /// this is <see cref="File.OpenRead"/> under the hood, which only opens a file handle — it does no
    /// awaited I/O, so a fake <c>Task</c>-returning signature would just be noise for every caller. The
    /// key is always server-generated from a Guid (never taken from client input), but the resolved path
    /// is still checked against the private root before opening — security here doesn't rest on the key
    /// being unguessable (US-19 p.4), and a row read back out of the database is not automatically
    /// trusted input.
    /// </summary>
    public Stream OpenPrivate(string storageKey)
    {
        var fullPath = ResolvePrivatePath(storageKey);
        if (fullPath is null || !File.Exists(fullPath))
            throw new FileNotFoundException("Private file not found", storageKey);

        return File.OpenRead(fullPath);
    }

    /// <summary>Deletes a private file by its storage key. A no-op if the key is empty or the file is
    /// already gone (the "delete DB row, then delete the file" ordering means a missing file here is an
    /// expected outcome of a previous partial failure, not an error — ARCHITECTURE.md §1.4).</summary>
    public void DeletePrivate(string? storageKey)
    {
        if (string.IsNullOrEmpty(storageKey)) return;
        var fullPath = ResolvePrivatePath(storageKey);
        if (fullPath is not null && File.Exists(fullPath)) File.Delete(fullPath);
    }

    /// <summary>
    /// Whether the drive holding the private root has at least <see cref="_minFreeDiskBytes"/> free,
    /// checked before every write (§4.1 step 7, §9.4) so a nearly-full disk fails an upload with a plain
    /// 400 instead of letting a mid-write IOException surface as an unhandled 500. The private root is
    /// what this actually protects: client-note photos are the volume driver (R1), while public uploads
    /// (logo/avatar/service image) are capped at one file per entity and are comparatively negligible.
    /// </summary>
    public bool HasFreeSpace(long neededBytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(PrivateRootFullPath) ?? PrivateRootFullPath);
        return drive.AvailableFreeSpace >= neededBytes + _minFreeDiskBytes;
    }

    /// <summary>Resolves a storage key to an absolute path, rejecting anything that would escape the
    /// private root (path traversal defence-in-depth — see class doc).</summary>
    private string? ResolvePrivatePath(string storageKey) => ResolveContained(PrivateRootFullPath, storageKey);

    /// <summary>Shared containment check behind both <see cref="ResolvePrivatePath"/> and
    /// <see cref="DeletePublic"/> (code review finding: the two used to implement this independently, and
    /// the public one was missing it entirely). Combines <paramref name="relativeOrKey"/> onto
    /// <paramref name="root"/> and returns the resulting absolute path only if it is still genuinely
    /// inside <paramref name="root"/> — <c>null</c> for anything that would escape it via <c>..</c>
    /// segments, an absolute path, or similar.</summary>
    private static string? ResolveContained(string root, string relativeOrKey)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, relativeOrKey.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? candidate : null;
    }

    private static string AreaFolder(PublicArea area) => area switch
    {
        PublicArea.Avatars => "avatars",
        PublicArea.Services => "services",
        PublicArea.Companies => "companies",
        _ => throw new ArgumentOutOfRangeException(nameof(area))
    };
}
