using System.Globalization;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §580, §579.1 — the "demo is being reset" flag: a small file at <see cref="DemoModeOptions.MaintenanceFlagPath"/> holding the time
/// the reset began. Written by <c>DemoResetService</c> (in the API process for the nightly task, in the <c>ops demo reset</c> process for the operator's
/// command — both see the same file, one container, one volume), read by <see cref="DemoMaintenanceMiddleware"/> and <c>GET /api/demo/status</c>.
///
/// The answer is cached for one second so a busy demo does not stat the file on every request. A flag older than <see cref="StaleAfter"/> is treated as
/// hung (the reset process died before removing it): it is ignored and reported once a minute with <c>LogError</c>, so a dead reset never holds the demo in
/// 503 (risk R28-15).
/// </summary>
public sealed class DemoMaintenanceFlag(IOptions<DemoModeOptions> options, IHostEnvironment environment, ILogger<DemoMaintenanceFlag> logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaleLogEvery = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private DateTime _checkedAtUtc = DateTime.MinValue;
    private bool _resetting;
    private DateTime _lastStaleLogUtc = DateTime.MinValue;

    public string FullPath => ResolvePath(options.Value.MaintenanceFlagPath, environment.ContentRootPath);

    /// <summary>A relative path is resolved against the content root, exactly like the other machine paths (<c>Storage:*</c>).</summary>
    public static string ResolvePath(string configured, string contentRootPath) =>
        Path.IsPathRooted(configured) ? configured : Path.GetFullPath(Path.Combine(contentRootPath, configured));

    /// <summary>Pure: what the flag file's content and age mean at <paramref name="nowUtc"/>. A missing file is (false, false); an unreadable time falls back to
    /// the file's own write time.</summary>
    public static FlagState Evaluate(bool exists, string? content, DateTime? lastWriteUtc, DateTime nowUtc)
    {
        if (!exists) return new FlagState(Resetting: false, Stale: false);
        DateTime? started = DateTime.TryParse(content?.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : lastWriteUtc;
        // No readable time at all: assume it is fresh (better 503 for a minute than a half-reset demo shown as ready); the next write time fixes it.
        if (started is not { } at) return new FlagState(Resetting: true, Stale: false);
        return nowUtc - at > StaleAfter ? new FlagState(Resetting: false, Stale: true) : new FlagState(Resetting: true, Stale: false);
    }

    public readonly record struct FlagState(bool Resetting, bool Stale);

    /// <summary>True while a reset is in progress (cached for a second).</summary>
    public bool IsResetting(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (nowUtc - _checkedAtUtc < CacheFor && nowUtc >= _checkedAtUtc) return _resetting;

            var path = FullPath;
            var exists = File.Exists(path);
            string? content = null;
            DateTime? lastWrite = null;
            if (exists)
            {
                try
                {
                    content = File.ReadAllText(path);
                    lastWrite = File.GetLastWriteTimeUtc(path);
                }
                catch (IOException)
                {
                    // The writer is replacing the file at this very moment: it exists, so the reset is on.
                }
            }

            var state = Evaluate(exists, content, lastWrite, nowUtc);
            if (state.Stale && nowUtc - _lastStaleLogUtc > StaleLogEvery)
            {
                _lastStaleLogUtc = nowUtc;
                logger.LogError("The demo reset flag {Path} is older than {Minutes} minutes: the reset process probably died; the flag is ignored. Remove the file and run `ops demo reset --yes`.",
                    path, StaleAfter.TotalMinutes);
            }

            _resetting = state.Resetting;
            _checkedAtUtc = nowUtc;
            return _resetting;
        }
    }

    /// <summary>Raises the flag (creates the folder when needed).</summary>
    public void Begin(DateTime nowUtc)
    {
        var path = FullPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, nowUtc.ToString("o", CultureInfo.InvariantCulture));
        Invalidate();
    }

    /// <summary>Removes the flag. A missing file is fine.</summary>
    public void End()
    {
        try { File.Delete(FullPath); }
        catch (DirectoryNotFoundException) { }
        Invalidate();
    }

    private void Invalidate()
    {
        lock (_gate) _checkedAtUtc = DateTime.MinValue;
    }
}
