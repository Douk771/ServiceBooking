using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Singleton in-memory cache of the legal documents manifest (ARCHITECTURE.md §4.3, extended by
/// ARCHITECTURE_CYCLE5.md §43.3 to two arrays — `documents` and `uiTexts`). Polls legal.json's mtime
/// (and every content file it references), not more often than LegalOptions.ReloadSeconds, and re-parses
/// everything atomically into a new LegalSnapshot on change — a FileSystemWatcher is deliberately not
/// used (inotify events aren't reliable across a Docker bind-mount boundary, and a silently-not-firing
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

    /// <summary>The one Cyrillic-uppercase placeholder pattern for the whole repository
    /// (ARCHITECTURE_CYCLE11.md §106.3) — <c>ServiceBooking.LegalKit</c>'s placeholder scanner is
    /// required to use this exact pattern rather than declaring its own, so the product's notion of "an
    /// unresolved placeholder" and the CLI's can never quietly diverge.</summary>
    public static string PlaceholderPattern => PlaceholderRegex().ToString();

    /// <summary>
    /// One load attempt that throws on any failure instead of silently keeping the previous snapshot
    /// (ARCHITECTURE_CYCLE11.md §104.2). <see cref="TryReload"/>/<see cref="EnsureFresh"/> are the
    /// product's read path and are NOT changed by this — they must keep serving the last good text no
    /// matter what's on disk right now. This method exists for callers that need the opposite: a tool
    /// (ServiceBooking.LegalKit) or a startup fail-fast check that wants the real exception, not a
    /// swallowed log line.
    /// </summary>
    public LegalSnapshot LoadStrict()
    {
        var manifestPath = Path.Combine(_root, "legal.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Legal documents manifest not found at {manifestPath}.");

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ManifestFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("legal.json parsed to null.");

        return LoadSnapshot(manifest.Documents ?? [], manifest.UiTexts ?? []);
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
            var documentEntries = manifest.Documents ?? [];
            var uiTextEntries = manifest.UiTexts ?? [];

            // max(mtime) over the manifest itself and every content file it references — see the field
            // comment on _lastLoadedSourcesMtimeUtc for why legal.json's own mtime alone isn't enough.
            // A missing/escaping file here just means "not fresher than before" for this check; the real
            // validation (and its error) happens in LoadDocument/LoadUiText below.
            var sourcesMtimeUtc = File.GetLastWriteTimeUtc(manifestPath);
            foreach (var file in documentEntries.Select(e => e.File).Concat(uiTextEntries.Select(e => e.File)))
            {
                var candidatePath = ResolveContentPath(file);
                if (candidatePath is not null && File.Exists(candidatePath))
                {
                    var fileMtimeUtc = File.GetLastWriteTimeUtc(candidatePath);
                    if (fileMtimeUtc > sourcesMtimeUtc) sourcesMtimeUtc = fileMtimeUtc;
                }
            }

            if (_snapshot is not null && sourcesMtimeUtc == _lastLoadedSourcesMtimeUtc)
                return; // nothing changed since the last successful load

            var snapshot = LoadSnapshot(documentEntries, uiTextEntries);
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
    internal static bool IsBareFilename([NotNullWhen(true)] string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !fileName.Contains('/')
        && !fileName.Contains('\\')
        && !fileName.Contains("..")
        && !Path.IsPathRooted(fileName);

    private LegalSnapshot LoadSnapshot(List<ManifestEntry> documentEntries, List<ManifestUiTextEntry> uiTextEntries)
    {
        var documents = new Dictionary<LegalDocumentType, LegalDocument>();
        foreach (var entry in documentEntries)
        {
            var doc = LoadDocument(entry);
            documents[doc.Type] = doc;
        }

        // All five document types are required to publish ANY of them — a manifest missing one is
        // exactly as broken as one with an invalid entry (ARCHITECTURE.md §4.4, ARCHITECTURE_CYCLE5.md
        // §43.3): partial legal coverage is not a valid state to serve to real users.
        var missingDocumentTypes = Enum.GetValues<LegalDocumentType>().Except(documents.Keys).ToList();
        if (missingDocumentTypes.Count > 0)
            throw new InvalidOperationException(
                $"legal.json is missing required document type(s): {string.Join(", ", missingDocumentTypes)}.");

        var uiTexts = new Dictionary<string, LegalUiText>(StringComparer.Ordinal);
        foreach (var entry in uiTextEntries)
        {
            var text = LoadUiText(entry);
            uiTexts[text.Key] = text;
        }

        var missingUiTextKeys = LegalTextKey.All.Except(uiTexts.Keys).ToList();
        if (missingUiTextKeys.Count > 0)
            throw new InvalidOperationException(
                $"legal.json is missing required uiTexts key(s): {string.Join(", ", missingUiTextKeys)}.");

        return new LegalSnapshot(documents, uiTexts);
    }

    private LegalDocument LoadDocument(ManifestEntry entry)
    {
        if (!Enum.TryParse<LegalDocumentType>(entry.Type, ignoreCase: true, out var type))
            throw new InvalidOperationException($"Unknown legal document type '{entry.Type}'.");

        if (string.IsNullOrWhiteSpace(entry.Version) || entry.Version.Length > 64)
            throw new InvalidOperationException($"{type}: version must be non-empty and at most 64 characters.");

        if (entry.IsDraft && !entry.Version.EndsWith("-draft", StringComparison.Ordinal))
            throw new InvalidOperationException($"{type}: isDraft is true but version '{entry.Version}' does not end with '-draft'.");

        var (contentHtml, contentHash) = LoadContent(type.ToString(), entry.File);

        if (!entry.IsDraft && PlaceholderRegex().IsMatch(contentHtml))
            throw new InvalidOperationException(
                $"{type}: document content contains an unresolved {{{{PLACEHOLDER}}}} and isDraft is false " +
                "— a document with a hole cannot be published as final (ARCHITECTURE_CYCLE5.md §43.3).");

        if (!Enum.TryParse<LegalChangeKind>(entry.ChangeKind, ignoreCase: true, out var changeKind))
            changeKind = LegalChangeKind.Material; // unrecognized/absent → safe default, §4.2

        // Unrecognized/absent → Global, the safe default (ARCHITECTURE_CYCLE5.md §43.3): a manifest typo
        // must fail closed (block more callers) rather than silently stop gating an owner-only document.
        if (!Enum.TryParse<LegalGate>(entry.Gate, ignoreCase: true, out var gate))
            gate = LegalGate.Global;

        if (!DateOnly.TryParse(entry.EffectiveFrom, out var effectiveFrom))
            throw new InvalidOperationException($"{type}: effectiveFrom '{entry.EffectiveFrom}' is not a valid date.");

        var purposes = new List<LegalPurpose>();
        if (entry.Purposes is { Count: > 0 })
        {
            if (type != LegalDocumentType.PdnConsent)
                throw new InvalidOperationException($"{type}: 'purposes' is only valid on PdnConsent.");

            foreach (var p in entry.Purposes)
            {
                if (!Enum.TryParse<ConsentPurpose>(p.Key, ignoreCase: true, out var purposeKey))
                    throw new InvalidOperationException($"{type}: unknown purpose key '{p.Key}'.");
                if (string.IsNullOrWhiteSpace(p.Title))
                    throw new InvalidOperationException($"{type}: purpose '{p.Key}' is missing a title.");
                purposes.Add(new LegalPurpose(purposeKey, p.Title));
            }
        }
        else if (type == LegalDocumentType.PdnConsent)
        {
            throw new InvalidOperationException("PdnConsent: 'purposes' must be non-empty (ARCHITECTURE_CYCLE5.md §43.3).");
        }

        return new LegalDocument(type, entry.Title ?? "", entry.Version, effectiveFrom, entry.IsDraft, changeKind, gate, purposes, contentHtml, contentHash);
    }

    private LegalUiText LoadUiText(ManifestUiTextEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Key) || !LegalTextKey.All.Contains(entry.Key))
            throw new InvalidOperationException($"uiTexts: unknown or missing key '{entry.Key}' — must be one of {string.Join(", ", LegalTextKey.All)}.");

        if (string.IsNullOrWhiteSpace(entry.Version) || entry.Version.Length > 64)
            throw new InvalidOperationException($"uiTexts.{entry.Key}: version must be non-empty and at most 64 characters.");

        if (entry.IsDraft && !entry.Version.EndsWith("-draft", StringComparison.Ordinal))
            throw new InvalidOperationException($"uiTexts.{entry.Key}: isDraft is true but version '{entry.Version}' does not end with '-draft'.");

        var (contentHtml, contentHash) = LoadContent($"uiTexts.{entry.Key}", entry.File);

        // §43.3's placeholder gate is written against LegalDocumentType.Purposes in the architecture
        // text, but the underlying rule ("a hole on a live domain is worse than no document at all",
        // §13-bis) applies exactly as much to a consent form's text — HealthDataConsent/PhotoConsent/
        // GuardianConfirmation are read out loud to a real subject the same way D1/D2 are.
        if (!entry.IsDraft && PlaceholderRegex().IsMatch(contentHtml))
            throw new InvalidOperationException(
                $"uiTexts.{entry.Key}: content contains an unresolved {{{{PLACEHOLDER}}}} and isDraft is false.");

        return new LegalUiText(entry.Key, entry.Version, entry.IsDraft, contentHtml, contentHash);
    }

    private (string ContentHtml, string ContentHash) LoadContent(string label, string file)
    {
        if (!IsBareFilename(file))
            throw new InvalidOperationException($"{label}: 'file' must be a bare filename, no path segments.");

        var fullPath = Path.GetFullPath(Path.Combine(_root, file));
        var rootFull = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootFull, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label}: 'file' escapes the legal documents root.");
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"{label}: file '{file}' not found in {_root}.");

        var contentHtml = File.ReadAllText(fullPath);

        // Not a sanitizer — a predisposition check against an operator typo, not an untrusted user
        // input filter (ARCHITECTURE.md §4.4). Legal:Root is configuration-grade trust, same as
        // appsettings.Production.json.
        if (contentHtml.Contains("<script", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label}: content contains '<script'.");
        if (OnEventAttributeRegex().IsMatch(contentHtml))
            throw new InvalidOperationException($"{label}: content contains an on…= event handler attribute.");

        // Computed once, here, not per-acceptance (ARCHITECTURE_CYCLE5.md §44.2 p.2): the hash written
        // into every ConsentRecord for this document/text is this exact value, so an operator who swaps
        // the file without bumping the version leaves old journal rows pointing at a hash nothing on
        // disk matches any more — the mismatch IS the tripwire, not a bug to paper over.
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentHtml))).ToLowerInvariant();

        return (contentHtml, contentHash);
    }

    [GeneratedRegex(@"\son\w+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex OnEventAttributeRegex();

    // ARCHITECTURE_CYCLE5.md §43.3: "{{[А-ЯЁ_]+}}" — an unresolved Cyrillic-uppercase placeholder token.
    [GeneratedRegex(@"\{\{[А-ЯЁ_]+\}\}")]
    private static partial Regex PlaceholderRegex();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class ManifestFile
    {
        [JsonPropertyName("documents")]
        public List<ManifestEntry>? Documents { get; set; }

        [JsonPropertyName("uiTexts")]
        public List<ManifestUiTextEntry>? UiTexts { get; set; }
    }

    private sealed class ManifestEntry
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("effectiveFrom")] public string EffectiveFrom { get; set; } = "";
        [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
        [JsonPropertyName("changeKind")] public string? ChangeKind { get; set; }
        [JsonPropertyName("gate")] public string? Gate { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("purposes")] public List<ManifestPurposeEntry>? Purposes { get; set; }
    }

    private sealed class ManifestPurposeEntry
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
    }

    private sealed class ManifestUiTextEntry
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
        [JsonPropertyName("file")] public string File { get; set; } = "";
    }
}
