using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>Outcome of one <c>ops showcase …</c> command: the exit code and the report lines (API_CONTRACT_CYCLE28.md §602).</summary>
public sealed record ShowcaseCommandResult(int ExitCode, IReadOnlyList<string> Lines);

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.1, §575.5 — the operator's showcase commands: plan, create, recreate, delete. Changing commands run in ONE transaction under
/// the advisory lock <c>ops:showcase</c> (a second run gets exit code 4), so a re-seed is delete + create in a single commit: the catalog sees the old showcase
/// before it and the new one after it, never an empty window. Files are deleted only after the commit. Without <c>--yes</c> a changing command prints
/// its plan and changes nothing. Names and phones never appear in the output.
/// </summary>
public class ShowcaseCommands(
    AppDbContext db, ShowcaseGenerator generator, ShowcaseEraser eraser, ILogger<ShowcaseCommands> logger)
{
    public const int ExitOk = 0;
    public const int ExitRefused = 2;
    public const int ExitLockBusy = 4;

    /// <summary>The default 30 s command timeout is not enough for the DELETE steps on a busy machine or with stale planner statistics (a delete that needs
    /// 0.5 s with fresh statistics took 7 s without them); the whole changing command runs under the same budget the insert already has.</summary>
    private const int CommandTimeoutSeconds = 300;

    /// <summary>
    /// ARCHITECTURE_CYCLE35.md §35.9.6, API_CONTRACT_CYCLE35.md §35.26 — <c>--profile demo</c>. Only the PLAN exists: it prints what a demo reset would create (the salon line and the line of
    /// the shops of «Заказы»), changes nothing and needs no database (the graph is pure, the assets are files). Every changing command answers exit code 2: the demo profile is
    /// created by the reset of the demo only (<c>ops demo reset</c>), which takes both locks of the demo.
    /// </summary>
    public Task<ShowcaseCommandResult> RunDemoProfileAsync(OpsAction action, DateTime nowUtc)
    {
        if (action != OpsAction.ShowcasePlan)
            return Task.FromResult(new ShowcaseCommandResult(ExitRefused, [OpsCommandLine.DemoProfileRefusal]));

        var profile = ShowcaseProfile.Demo;
        var graph = ShowcaseDataset.Build(profile, nowUtc);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var (photos, files) = generator.CountAssets(graph);
        var lines = new List<string>
        {
            $"ops showcase plan — профиль {profile.Name}, сегодня {today:yyyy-MM-dd} (по поясу каждой компании)",
            $"будет создано: {graph.Counts(photos, files)} reviews={graph.Reviews.Count} clientNotes={graph.ClientNotes.Count}",
            $"будет создано (Заказы): {graph.OrdersCounts(generator.CountProductImages(graph))}",
            "режим: только показать (профиль demo создаётся только сбросом демо: ops demo reset --yes)",
        };
        return Task.FromResult(new ShowcaseCommandResult(ExitOk, lines));
    }

    public async Task<ShowcaseCommandResult> RunAsync(
        OpsAction action, ShowcasePlanKind? planOf, bool confirmed, DateTime nowUtc, CancellationToken ct)
    {
        var profile = ShowcaseProfile.Prod;
        var kind = action switch
        {
            OpsAction.ShowcaseCreate => ShowcasePlanKind.Create,
            OpsAction.ShowcaseRecreate => ShowcasePlanKind.Recreate,
            OpsAction.ShowcaseDelete => ShowcasePlanKind.Delete,
            _ => planOf ?? (await ExistsAsync(ct) ? ShowcasePlanKind.Recreate : ShowcasePlanKind.Create),
        };
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var title = $"ops showcase {kind.ToString().ToLowerInvariant()} — профиль {profile.Name}, сегодня {today:yyyy-MM-dd} (по поясу каждой компании)";

        return action == OpsAction.ShowcasePlan || !confirmed
            ? await PlanAsync(kind, profile, nowUtc, title, ct)
            : await ExecuteWithLongTimeoutAsync(kind, profile, nowUtc, title, ct);
    }

    private async Task<ShowcaseCommandResult> ExecuteWithLongTimeoutAsync(ShowcasePlanKind kind, ShowcaseProfile profile, DateTime nowUtc, string title, CancellationToken ct)
    {
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(CommandTimeoutSeconds));
        try
        {
            return await ExecuteAsync(kind, profile, nowUtc, title, ct);
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    private async Task<ShowcaseCommandResult> PlanAsync(ShowcasePlanKind kind, ShowcaseProfile profile, DateTime nowUtc, string title, CancellationToken ct)
    {
        var lines = new List<string> { title };
        var exists = await ExistsAsync(ct);
        if (kind != ShowcasePlanKind.Create)
            lines.Add($"будет удалено: {await eraser.CountAsync(ct)}");
        if (kind != ShowcasePlanKind.Delete)
        {
            var graph = ShowcaseDataset.Build(profile, nowUtc);
            var (photos, files) = generator.CountAssets(graph);
            lines.Add($"будет создано: {graph.Counts(photos, files)}");
            if (kind == ShowcasePlanKind.Create && exists)
                lines.Add("внимание: витрина уже есть — `create` завершится кодом 2, используйте `recreate`");
        }
        if (kind == ShowcasePlanKind.Delete && !exists)
            lines.Add("витрины нет, удалять нечего");
        lines.Add("режим: только показать (добавьте --yes, чтобы выполнить)");
        return new ShowcaseCommandResult(ExitOk, lines);
    }

    private async Task<ShowcaseCommandResult> ExecuteAsync(ShowcasePlanKind kind, ShowcaseProfile profile, DateTime nowUtc, string title, CancellationToken ct)
    {
        var lines = new List<string> { title };
        var stopwatch = Stopwatch.StartNew();
        ShowcaseEraseResult? erased = null;
        ShowcaseGraph? graph = null;
        int photos = 0, files = 0;

        try
        {
            // Checks that need no lock and no transaction come first, so a refusal leaves nothing behind.
            IReadOnlyDictionary<string, int>? cityIds = null;
            if (kind != ShowcasePlanKind.Delete)
            {
                cityIds = await generator.ResolveCitiesAsync(ct);
                graph = ShowcaseDataset.Build(profile, nowUtc);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (!await AdvisoryLock.TryAcquireAsync(db, ShowcaseCatalog.LockKey))
                return new ShowcaseCommandResult(ExitLockBusy, [.. lines, "Замок ops:showcase занят: идёт другой запуск."]);

            if (kind == ShowcasePlanKind.Create && await ExistsAsync(ct))
                return new ShowcaseCommandResult(ExitRefused,
                    [.. lines, "Витрина уже создана. Пересоздать: `ops showcase recreate --yes`; удалить: `ops showcase delete --yes`."]);

            if (kind != ShowcasePlanKind.Create)
                erased = await eraser.EraseAsync(ct);
            if (kind != ShowcasePlanKind.Delete)
            {
                await generator.PersistAsync(graph!, cityIds!, profile.Name, ct);
                (photos, files) = generator.CountAssets(graph!);
                await StampLastReseedAsync(nowUtc, ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (ShowcaseRefusedException ex)
        {
            return new ShowcaseCommandResult(ExitRefused, [.. lines, ex.Message]);
        }

        // After the commit only: a rollback must never leave rows pointing at missing files.
        if (erased is not null)
            eraser.DeleteFilesAfterCommit(erased.FilesToDelete, graph is null ? null : generator.PublishedFileNames);

        stopwatch.Stop();
        var summary = new List<string>();
        if (erased is not null) summary.Add($"удалено: {erased.Counts}");
        if (graph is not null) summary.Add($"создано: {graph.Counts(photos, files)}");
        lines.Add($"выполнено: {string.Join("; ", summary)} за {stopwatch.Elapsed:mm\\:ss}");
        logger.LogInformation("ops showcase {Kind} done in {Elapsed}", kind, stopwatch.Elapsed);
        return new ShowcaseCommandResult(ExitOk, lines);
    }

    /// <summary>Records when the showcase was last (re)created, in the same transaction — the weekly re-seed task counts from it (§575.7).</summary>
    private async Task StampLastReseedAsync(DateTime nowUtc, CancellationToken ct)
    {
        var value = nowUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        var setting = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == ShowcaseCatalog.LastReseedKey, ct);
        if (setting is null)
            db.PlatformSettings.Add(new Core.Entities.PlatformSetting { Key = ShowcaseCatalog.LastReseedKey, Value = value, UpdatedAt = nowUtc, UpdatedByUserId = "ops" });
        else
        {
            setting.Value = value;
            setting.UpdatedAt = nowUtc;
            setting.UpdatedByUserId = "ops";
        }
        await db.SaveChangesAsync(ct);
    }

    private Task<bool> ExistsAsync(CancellationToken ct) => db.Companies.AnyAsync(c => c.IsShowcase, ct);
}
