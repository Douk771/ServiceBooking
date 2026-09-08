using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Singleton in-memory cache of the legal documents manifest (ARCHITECTURE.md §4.3). Polls
/// legal.json's mtime, not more often than LegalOptions.ReloadSeconds, and re-parses everything
/// atomically into a new LegalSnapshot on change — a FileSystemWatcher is deliberately not used
/// (inotify events aren't reliable across a Docker bind-mount boundary, and a silently-not-firing
/// watcher is exactly the failure this history must not have).
///
/// A failed reload (missing file, invalid manifest, a document that fails validation) NEVER clears the
/// previous good snapshot — the operator broke the working copy on disk, the product keeps serving the
/// last text that was actually valid, and the failure is logged at Error (reaches the tracker, §11).
/// </summary>
public partial class LegalDocumentProvider
{
    private readonly LegalOptions _options;
    private readonly string _root;
    private readonly ILogger<LegalDocumentProvider> _logger;
    private readonly object _reloadLock = new();

    private volatile LegalSnapshot? _snapshot;
    private DateTime _lastCheckedUtc = DateTime.MinValue;
    private DateTime _lastLoadedManifestMtimeUtc = DateTime.MinValue;

    public LegalDocumentProvider(IOptions<LegalOptions> options, IWebHostEnvironment env, ILogger<LegalDocumentProvider> logger)
    {
        _options = options.Value;
        _root = string.IsNullOrWhiteSpace(_options.Root)
            ? Path.Combine(env.ContentRootPath, "App_Data", "legal")
            : _options.Root;
        _logger = logger;
    }

    /// <summary>Last successfully loaded snapshot, or null if none has ever loaded successfully.</summary>
    public LegalSnapshot? Current
    {
        get
        {
            EnsureFresh();
            return _snapshot;
        }
    }

    /// <summary>
    /// Forces an immediate load attempt outside the ReloadSeconds cache window, for Program.cs's
    /// startup fail-fast (ARCHITECTURE.md §4.4/§13) — without this, the very first request would pay
    /// for the initial parse anyway, but the app would already have finished starting by then.
    /// </summary>
    public void LoadAtStartup()
    {
        lock (_reloadLock)
        {
            _lastCheckedUtc = DateTime.UtcNow;
            TryReload();
        }
    }

    private void EnsureFresh()
    {
        var intervalSeconds = Math.Max(1, _options.ReloadSeconds);
        if ((DateTime.UtcNow - _lastCheckedUtc).TotalSeconds < intervalSeconds) return;

        lock (_reloadLock)
        {
            if ((DateTime.UtcNow - _lastCheckedUtc).TotalSeconds < intervalSeconds) return; // lost the race, someone else just refreshed
            _lastCheckedUtc = DateTime.UtcNow;
            TryReload();
        }
    }

    private void TryReload()
    {
        var manifestPath = Path.Combine(_root, "legal.json");
        try
        {
            if (!File.Exists(manifestPath))
            {
                _logger.LogError("Legal documents manifest not found at {Path}", manifestPath);
                return;
            }

            var mtimeUtc = File.GetLastWriteTimeUtc(manifestPath);
            if (_snapshot is not null && mtimeUtc == _lastLoadedManifestMtimeUtc)
                return; // nothing changed since the last successful load

            var snapshot = LoadSnapshot(manifestPath);
            _snapshot = snapshot;
            _lastLoadedManifestMtimeUtc = mtimeUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to (re)load the legal documents manifest at {Path} — keeping the previous snapshot", manifestPath);
        }
    }

    private LegalSnapshot LoadSnapshot(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ManifestFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("legal.json parsed to null.");

        var documents = new Dictionary<LegalDocumentType, LegalDocument>();
        foreach (var entry in manifest.Documents ?? [])
        {
            var doc = LoadDocument(entry);
            documents[doc.Type] = doc;
        }

        // Both documents are required to publish ANY of them — a manifest missing one is exactly as
        // broken as one with an invalid entry (ARCHITECTURE.md §4.4): partial legal coverage is not a
        // valid state to serve to real users.
        if (!documents.ContainsKey(LegalDocumentType.Privacy) || !documents.ContainsKey(LegalDocumentType.Terms))
            throw new InvalidOperationException("legal.json must contain both a Privacy and a Terms document.");

        return new LegalSnapshot(documents);
    }

    private LegalDocument LoadDocument(ManifestEntry entry)
    {
        if (!Enum.TryParse<LegalDocumentType>(entry.Type, ignoreCase: true, out var type))
            throw new InvalidOperationException($"Unknown legal document type '{entry.Type}'.");

        if (string.IsNullOrWhiteSpace(entry.Version) || entry.Version.Length > 64)
            throw new InvalidOperationException($"{type}: version must be non-empty and at most 64 characters.");

        if (entry.IsDraft && !entry.Version.EndsWith("-draft", StringComparison.Ordinal))
            throw new InvalidOperationException($"{type}: isDraft is true but version '{entry.Version}' does not end with '-draft'.");

        if (string.IsNullOrWhiteSpace(entry.File) || entry.File.Contains('/') || entry.File.Contains('\\') || entry.File.Contains(".."))
            throw new InvalidOperationException($"{type}: 'file' must be a bare filename, no path segments.");

        var fullPath = Path.GetFullPath(Path.Combine(_root, entry.File));
        var rootFull = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootFull, StringComparison.Ordinal))
            throw new InvalidOperationException($"{type}: 'file' escapes the legal documents root.");
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"{type}: file '{entry.File}' not found in {_root}.");

        var contentHtml = File.ReadAllText(fullPath);

        // Not a sanitizer — a predisposition check against an operator typo, not an untrusted user
        // input filter (ARCHITECTURE.md §4.4). Legal:Root is configuration-grade trust, same as
        // appsettings.Production.json.
        if (contentHtml.Contains("<script", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{type}: document content contains '<script'.");
        if (OnEventAttributeRegex().IsMatch(contentHtml))
            throw new InvalidOperationException($"{type}: document content contains an on…= event handler attribute.");

        if (!Enum.TryParse<LegalChangeKind>(entry.ChangeKind, ignoreCase: true, out var changeKind))
            changeKind = LegalChangeKind.Material; // unrecognized/absent → safe default, §4.2

        if (!DateOnly.TryParse(entry.EffectiveFrom, out var effectiveFrom))
            throw new InvalidOperationException($"{type}: effectiveFrom '{entry.EffectiveFrom}' is not a valid date.");

        return new LegalDocument(type, entry.Title ?? "", entry.Version, effectiveFrom, entry.IsDraft, changeKind, contentHtml);
    }

    [GeneratedRegex(@"\son\w+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex OnEventAttributeRegex();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class ManifestFile
    {
        [JsonPropertyName("documents")]
        public List<ManifestEntry>? Documents { get; set; }
    }

    private sealed class ManifestEntry
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("effectiveFrom")] public string EffectiveFrom { get; set; } = "";
        [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
        [JsonPropertyName("changeKind")] public string? ChangeKind { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("file")] public string File { get; set; } = "";
    }
}
