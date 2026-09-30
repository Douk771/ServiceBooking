using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Startup;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Startup;

/// <summary>
/// Migrations, roles and the SuperAdmin account (with its consent records) at startup. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// Cycle 28, pass B: the roles and the SuperAdmin moved, verbatim, into <see cref="SuperAdminSeeder"/> (the demo reset seeds them again after it wipes the database);
/// the order is the same, and the data lock of demo mode (<see cref="DemoInstanceGuard"/>) runs right after the migrations, before any seeding.
/// </summary>
internal static class StartupSeedingExtensions
{
    public static async Task SeedAsync(this WebApplication app, WebApplicationBuilder builder)
    {
    // Seed roles and super-admin on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        // ARCHITECTURE_CYCLE28.md §579.2, lock 2 — the data lock of demo mode: after migrations, before seeding. Refuses to start on a database that is marked as
        // another kind of instance or holds real (unmarked) data; writes the mark `instance.kind = demo` on a fresh empty one. A production instance does not touch it.
        if (scope.ServiceProvider.GetRequiredService<IOptions<DemoModeOptions>>().Value.Enabled)
            await DemoInstanceGuard.EnsureDemoDatabaseAsync(db, scope.ServiceProvider.GetRequiredService<ILogger<Program>>());

        // ARCHITECTURE_CYCLE19.md §385.5 — second line of defence behind the deploy-time gate; never
        // blocks startup, just makes an otherwise-invisible impossible state loud.
        await ServiceBooking.API.Services.Billing.RetiredLimitOptionsStartupReport.LogAsync(
            db, scope.ServiceProvider.GetRequiredService<ILogger<Program>>());

        await scope.ServiceProvider.GetRequiredService<SuperAdminSeeder>().EnsureRolesAndSuperAdminAsync();
    }
    }
}
