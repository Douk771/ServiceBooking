using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Enums;

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

    // Deliberately max(mtime) over legal.json AND every content file it references, not just
    // legal.json's own mtime. An operator fixing a typo in privacy.html without touching legal.json
    // (no version bump, US-36 p.5 doesn't require one for an Editorial fix) used to be invisible to
    // this check — the manifest's mtime hadn't moved, so the stale text kept being served until the
    // next unrelated manifest edit or a restart. That's exactly the failure ARCHITECTURE.md §4.3
    // declares excluded.
    private DateTime _lastLoadedSourcesMtimeUtc = DateTime.MinValue;

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

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ManifestFile>(json, JsonOptions)
                ?? throw new InvalidOperationException("legal.json parsed to null.");
            var entries = manifest.Documents ?? [];

            // max(mtime) over the manifest itself and every content file it references — see the field
            // comment on _lastLoadedSourcesMtimeUtc for why legal.json's own mtime alone isn't enough.
            // A missing/escaping file here just means "not fresher than before" for this check; the real
            // validation (and its error) happens in LoadDocument below.
            var sourcesMtimeUtc = File.GetLastWriteTimeUtc(manifestPath);
            foreach (var entry in entries)
            {
                var candidatePath = ResolveContentPath(entry.File);
                if (candidatePath is not null && File.Exists(candidatePath))
                {
                    var fileMtimeUtc = File.GetLastWriteTimeUtc(candidatePath);
                    if (fileMtimeUtc > sourcesMtimeUtc) sourcesMtimeUtc = fileMtimeUtc;
                }
            }

            if (_snapshot is not null && sourcesMtimeUtc == _lastLoadedSourcesMtimeUtc)
                return; // nothing changed since the last successful load

            var snapshot = LoadSnapshot(entries);
            _snapshot = snapshot;
            _lastLoadedSourcesMtimeUtc = sourcesMtimeUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to (re)load the legal documents manifest at {Path} — keeping the previous snapshot", manifestPath);
        }
    }

    /// <summary>Best-effort path resolution for the freshness check only — not a substitute for the
    /// strict containment check LoadDocument performs before ever reading a file as content.</summary>
    private string? ResolveContentPath(string? fileName)
    {
        if (!IsBareFilename(fileName)) return null;
        return Path.Combine(_root, fileName);
    }

    /// <summary>
    /// The one "no path segments, no traversal" rule shared by ResolveContentPath (freshness check)
    /// and LoadDocument (actual content read). A rooted/absolute fileName (e.g. "/etc/legal.html" or
    /// "C:\legal.html") contains neither ".." nor a separator relative to itself, so checking those
    /// alone would let ResolveContentPath escape _root entirely — not a content-disclosure risk (mtime
    /// only, and LoadDocument's own copy of this check would still reject it as content), but it would
    /// make the freshness poll silently track an unrelated file's mtime instead. One rule, not two.
    /// </summary>
    internal static bool IsBareFilename(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !fileName.Contains('/')
        && !fileName.Contains('\\')
        && !fileName.Contains("..")
        && !Path.IsPathRooted(fileName);

    private LegalSnapshot LoadSnapshot(List<ManifestEntry> entries)
    {
        var documents = new Dictionary<LegalDocumentType, LegalDocument>();
        foreach (var entry in entries)
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

        if (!IsBareFilename(entry.File))
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
