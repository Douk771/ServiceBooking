using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Ops;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.1 — runs one operator command inside the already built host (the same EF context, options and
/// services as the web server, but no middleware, no background tasks and no seeding). Reports go to <c>output</c> line by line so that
/// DEPLOY.md and the tests can grep them; exit codes are <see cref="OpsCommandLine.Help"/>.
/// There is deliberately no HTTP or anonymous route to any of this.
/// </summary>
public static class OpsCommandRunner
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitRefused = 2;
    public const int ExitPendingMigrations = 3;
    public const int ExitLockBusy = 4;

    public static async Task<int> RunAsync(IServiceProvider services, OpsCommandLine cli, TextWriter output, CancellationToken ct = default)
    {
        if (cli.Action is not { } action)
        {
            await output.WriteLineAsync(cli.Error ?? "Неизвестная команда.");
            await output.WriteLineAsync(OpsCommandLine.Help);
            return OpsCommandLine.ExitUnknownCommand;
        }

        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Ops");
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Migrations are applied by the running API at its start (StartupSeedingExtensions). Commands never migrate: two
            // processes migrating one database at once is exactly what this refuses to start.
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                await output.WriteLineAsync($"Есть непримененные миграции ({pending.Count}): сначала выкатите или перезапустите API.");
                return ExitPendingMigrations;
            }

            return action switch
            {
                OpsAction.TariffsPlan => await RunTariffsAsync(scope.ServiceProvider, apply: false, output, ct),
                OpsAction.TariffsApply => await RunTariffsAsync(scope.ServiceProvider, apply: true, output, ct),
                OpsAction.DemoReset => await RunDemoResetAsync(scope.ServiceProvider, output),
                _ => await RunShowcaseAsync(scope.ServiceProvider, cli, output, ct),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ops {Action} failed", action);
            await output.WriteLineAsync($"ошибка: {ex.GetType().Name}: {ex.Message}");
            return ExitError;
        }
    }

    private static async Task<int> RunTariffsAsync(IServiceProvider sp, bool apply, TextWriter output, CancellationToken ct)
    {
        var seeder = sp.GetRequiredService<TariffCatalogSeeder>();
        var report = apply ? await seeder.ApplyAsync(ct) : await seeder.PlanAsync(ct);
        if (report.LockBusy)
        {
            await output.WriteLineAsync("Замок ops:tariffs занят: идёт другой запуск.");
            return ExitLockBusy;
        }
        await output.WriteLineAsync($"ops tariffs {(apply ? "apply" : "plan")}");
        foreach (var line in report.Lines) await output.WriteLineAsync(line);
        return ExitOk;
    }

    private static async Task<int> RunDemoResetAsync(IServiceProvider sp, TextWriter output)
    {
        // The demo stand is pass B of cycle 28 (BE-9/BE-10). Until then the command exists only so that the negative case of the
        // contract holds: on a production configuration it refuses with code 2.
        var enabled = sp.GetRequiredService<IConfiguration>().GetValue("DemoMode:Enabled", false);
        await output.WriteLineAsync(enabled
            ? "Демо-стенд не реализован в этой сборке (проход B цикла 28)."
            : "Демо-режим не включён (DemoMode:Enabled=false): сброс невозможен.");
        return ExitRefused;
    }

    private static Task<int> RunShowcaseAsync(IServiceProvider sp, OpsCommandLine cli, TextWriter output, CancellationToken ct) =>
        Task.FromResult(OpsCommandLine.ExitUnknownCommand);
}
