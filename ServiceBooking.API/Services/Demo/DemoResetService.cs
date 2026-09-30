using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.API.Services.Startup;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Demo;

/// <summary>Outcome of one demo reset: the exit code of <c>ops demo reset</c> (0 done, 1 failed, 2 refused, 4 lock busy) and the report lines.</summary>
public sealed record DemoResetResult(int ExitCode, IReadOnlyList<string> Lines);

/// <summary>
/// ARCHITECTURE_CYCLE28.md §580 (US-28-11, A8) — the ONE procedure behind both the nightly <c>demo-reset</c> task and the operator's <c>ops demo reset --yes</c>.
///
/// <list type="number">
/// <item>Both locks (§579.2): <c>DemoMode:Enabled</c> and the database mark <c>instance.kind = demo</c>. Anything else is refused (exit 2) before a single byte is touched —
/// a production instance can never get here.</item>
/// <item>One transaction under the advisory lock <c>ops:showcase</c> (a second run gets exit 4): raise the maintenance flag (the API answers 503 «Демо обновляется»),
/// TRUNCATE every table of the model except the allow-list (<see cref="DemoResetTables"/>), re-create the tariffs of the grid, create the demo showcase
/// (<see cref="ShowcaseProfile.Demo"/>), publish the price list. Commit. A failure anywhere rolls the WHOLE transaction back — TRUNCATE is transactional in
/// PostgreSQL — so a failed reset leaves yesterday's demo intact.</item>
/// <item>After the commit: wipe the file storage (except the pictures of the new data), put the SuperAdmin back (<see cref="SuperAdminSeeder"/> — the same code the
/// start uses; it opens transactions of its own, which is why it cannot run inside the big one), stamp <c>demo.last-reset-utc</c>, lower the flag.</item>
/// </list>
/// </summary>
public sealed class DemoResetService(
    AppDbContext db,
    IOptions<DemoModeOptions> options,
    DemoMaintenanceFlag maintenanceFlag,
    TariffCatalogSeeder tariffSeeder,
    ShowcaseGenerator generator,
    SuperAdminSeeder superAdminSeeder,
    FileStorage storage,
    ILogger<DemoResetService> logger)
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitRefused = 2;
    public const int ExitLockBusy = 4;

    /// <summary>Same budget as the showcase commands: bulk inserts and the TRUNCATE must not be cut by the default 30 s.</summary>
    private const int CommandTimeoutSeconds = 300;

    /// <summary>After raising the flag, let the API process notice it (its answer is cached for a second) and let requests already in flight finish, so the
    /// TRUNCATE does not queue behind them.</summary>
    private static readonly TimeSpan FlagPropagationDelay = TimeSpan.FromMilliseconds(1300);

    /// <summary>How long the TRUNCATE waits for a background task that is still reading a table before giving up (and rolling back).</summary>
    private const string LockTimeout = "60s";

    public async Task<DemoResetResult> ResetAsync(DateTime nowUtc, bool confirmed, CancellationToken ct)
    {
        var profile = ShowcaseProfile.Demo;
        var title = $"ops demo reset — профиль {profile.Name}, сегодня {DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ZoneOrMoscow())):yyyy-MM-dd}";
        var lines = new List<string> { title };

        // Lock 1 and lock 2 (§579.2): the demo reset is impossible on any other instance.
        if (!options.Value.Enabled)
            return Refused(lines, "Демо-режим не включён (DemoMode:Enabled=false): сброс невозможен.");
        if (!await DemoInstanceGuard.IsDemoDatabaseAsync(db, ct))
            return Refused(lines, $"База не помечена как демо ({DemoCatalog.InstanceKindKey} ≠ {DemoCatalog.InstanceKindDemo}): сброс невозможен. Сначала запустите API в демо-режиме на пустой демо-базе.");

        Dictionary<string, int> cityIds;
        try
        {
            cityIds = await generator.ResolveCitiesAsync(ct);
        }
        catch (ShowcaseRefusedException ex)
        {
            return Refused(lines, ex.Message);
        }
        var graph = ShowcaseDataset.Build(profile, nowUtc);
        var tables = DemoResetTables.TablesToWipe(db.Model);

        if (!confirmed)
        {
            lines.Add($"будет очищено таблиц: {tables.Count} (кроме справочников: города, роли, тарифы, настройки платформы), и файловые хранилища демо");
            var (plannedPhotos, plannedFiles) = generator.CountAssets(graph);
            lines.Add($"будет создано: {graph.Counts(plannedPhotos, plannedFiles)} reviews={graph.Reviews.Count} clientNotes={graph.ClientNotes.Count}");
            // API_CONTRACT_CYCLE35.md §35.26: the shops of «Заказы» have their own line; the first one keeps its format.
            if (graph.HasShops) lines.Add($"будет создано (Заказы): {graph.OrdersCounts(generator.CountProductImages(graph))}");
            lines.Add("режим: только показать (добавьте --yes, чтобы выполнить)");
            return new DemoResetResult(ExitOk, lines);
        }

        var stopwatch = Stopwatch.StartNew();
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(CommandTimeoutSeconds));
        var flagRaised = false;
        try
        {
            int photos, files;
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                if (!await AdvisoryLock.TryAcquireAsync(db, ShowcaseCatalog.LockKey))
                    return new DemoResetResult(ExitLockBusy, [.. lines, "Замок ops:showcase занят: идёт другой запуск."]);

                // The flag is raised only by the holder of the lock, and lowered only by it (finally below).
                maintenanceFlag.Begin(nowUtc);
                flagRaised = true;
                await Task.Delay(FlagPropagationDelay, ct);

                await db.Database.ExecuteSqlRawAsync($"SET LOCAL lock_timeout = '{LockTimeout}'", ct);
                await db.Database.ExecuteSqlRawAsync(DemoResetTables.TruncateSql(tables), ct);
                db.ChangeTracker.Clear();

                await tariffSeeder.ApplyAsync(ct);
                await generator.PersistAsync(graph, cityIds, profile.Name, ct);
                (photos, files) = generator.CountAssets(graph);
                await UpsertSettingAsync(PricingCatalogCache.PublicEnabledSettingKey, "true", nowUtc, ct);
                await db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
            }

            // After the commit only: a rollback above must never leave rows pointing at wiped files.
            var deletedFiles = storage.ClearAllFiles(generator.PublishedFileNames);
            await superAdminSeeder.EnsureRolesAndSuperAdminAsync();
            await UpsertSettingAsync(DemoCatalog.LastResetKey, nowUtc.ToString("o", CultureInfo.InvariantCulture), nowUtc, ct);
            await db.SaveChangesAsync(ct);

            stopwatch.Stop();
            lines.Add($"выполнено: очищено таблиц={tables.Count} файлов={deletedFiles}; создано: {graph.Counts(photos, files)} reviews={graph.Reviews.Count} clientNotes={graph.ClientNotes.Count} за {stopwatch.Elapsed:mm\\:ss}");
            if (graph.HasShops) lines.Add($"выполнено (Заказы): {graph.OrdersCounts(generator.CountProductImages(graph))}");
            logger.LogInformation("demo reset done in {Elapsed}: {Tables} tables wiped, {Files} files deleted", stopwatch.Elapsed, tables.Count, deletedFiles);
            return new DemoResetResult(ExitOk, lines);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "demo reset failed; the transaction (if it was open) is rolled back");
            return new DemoResetResult(ExitError, [.. lines, $"ошибка: {ex.GetType().Name}: {ex.Message}"]);
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
            if (flagRaised) maintenanceFlag.End();
        }
    }

    private async Task UpsertSettingAsync(string key, string value, DateTime nowUtc, CancellationToken ct)
    {
        var setting = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
            db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = value, UpdatedAt = nowUtc, UpdatedByUserId = DemoCatalog.StampedBy });
        else
        {
            setting.Value = value;
            setting.UpdatedAt = nowUtc;
            setting.UpdatedByUserId = DemoCatalog.StampedBy;
        }
    }

    private static DemoResetResult Refused(List<string> lines, string message) => new(ExitRefused, [.. lines, message]);

    private TimeZoneInfo ZoneOrMoscow()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); }
    }
}
