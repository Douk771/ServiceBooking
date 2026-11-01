using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// CY49-D1 (ARCHITECTURE_CYCLE49_LISTING_SUBSCRIPTION.md §49.7.3, DO-49-2): deploy/checks/cycle49-catalog-*.sql agree with the code.
/// "After" = <see cref="SalonListingQuery.VisibleInSalonCatalog"/>; "now" = the same filter without the two new EXISTS
/// (Kind/IsActive/ShowInPublicListing + WhereAllowsPublicListing). cycle49-catalog-empty.sql is a bare SELECT (Npgsql) and must return a row
/// only when visible_now > 0 and visible_after = 0; the report uses psql meta-commands and runs in the container's psql
/// (skipped when no docker container publishes the test Postgres port, as CY48-D4).
/// One test method with phases, because the class database is shared by the tests of the class.
/// Goes green after BE-49-1 (the new VisibleInSalonCatalog).
/// </summary>
public class Cycle49CatalogReportSqlTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private static string FindDeployCheck(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "deploy", "checks", file);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"deploy/checks/{file} not found above {AppContext.BaseDirectory}");
    }

    private async Task<(Guid Id, string Slug, string OwnerUserId)> NewSalonAsync(string prefix)
    {
        var registered = await RegisterAsync();
        var company = await CreateCompanyAsync(registered.Token, slug: Unique(prefix));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = await db.Companies.SingleAsync(x => x.Id == company.Id);
        c.IsActive = true;
        c.ShowInPublicListing = true;
        // clean slate: no services, owner member provides services (default)
        db.Services.RemoveRange(db.Services.Where(s => s.CompanyId == c.Id));
        await db.SaveChangesAsync();
        return (c.Id, c.Slug, c.OwnerUserId);
    }

    private async Task ConfigureAsync(Guid companyId, bool activeService, bool inactiveService, bool ownerProvides)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (activeService)
            db.Services.Add(new Service { Id = Guid.NewGuid(), CompanyId = companyId, Name = "A", DurationMinutes = 30, Price = 1, IsActive = true });
        if (inactiveService)
            db.Services.Add(new Service { Id = Guid.NewGuid(), CompanyId = companyId, Name = "I", DurationMinutes = 30, Price = 1, IsActive = false });
        foreach (var m in db.CompanyMembers.Where(m => m.CompanyId == companyId && m.Role == UserRole.CompanyOwner))
            m.ProvidesServices = ownerProvides;
        await db.SaveChangesAsync();
    }

    private async Task<bool> StopReturnsRowAsync()
    {
        var sql = await File.ReadAllTextAsync(FindDeployCheck("cycle49-catalog-empty.sql"));
        sql.Split('\n').Where(l => l.TrimStart().StartsWith('\\')).Should().BeEmpty("the gate file must stay Npgsql-executable");
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync();
    }

    private async Task<(HashSet<string> Now, HashSet<string> After)> CodeSetsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var after = await db.Companies.VisibleInSalonCatalog(db, now).Select(c => c.Slug).ToListAsync();
        var before = await db.Companies
            .Where(c => c.Kind == CompanyKind.Services && c.IsActive && c.ShowInPublicListing)
            .WhereAllowsPublicListing(db, now)
            .Select(c => c.Slug).ToListAsync();
        return (before.ToHashSet(), after.ToHashSet());
    }

    [Fact, TestCase("CY49-D1")]
    public async Task CatalogSql_AgreesWithCode_AndStopFiresOnlyWhenAllSalonsDisappear()
    {
        // Phase 1: only salons that the new rule hides -> visible_now > 0, visible_after = 0 -> the stop returns a row.
        var noServices = await NewSalonAsync("c49-nosvc-");
        await ConfigureAsync(noServices.Id, activeService: false, inactiveService: true, ownerProvides: true);
        var noProviders = await NewSalonAsync("c49-noprov-");
        await ConfigureAsync(noProviders.Id, activeService: true, inactiveService: false, ownerProvides: false);
        var neither = await NewSalonAsync("c49-none-");
        await ConfigureAsync(neither.Id, activeService: false, inactiveService: false, ownerProvides: false);

        var (nowSet, afterSet) = await CodeSetsAsync();
        nowSet.Should().Contain([noServices.Slug, noProviders.Slug, neither.Slug]);
        afterSet.Should().BeEmpty();
        (await StopReturnsRowAsync()).Should().BeTrue("every currently visible salon disappears");

        // Phase 2: one salon passes the new rule -> no stop; other salons stay hidden for their own reasons.
        var good = await NewSalonAsync("c49-good-");
        await ConfigureAsync(good.Id, activeService: true, inactiveService: true, ownerProvides: true);
        var hiddenByFlag = await NewSalonAsync("c49-flag-");
        await ConfigureAsync(hiddenByFlag.Id, activeService: true, inactiveService: false, ownerProvides: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Companies.SingleAsync(c => c.Id == hiddenByFlag.Id)).ShowInPublicListing = false;
            await db.SaveChangesAsync();
        }

        (nowSet, afterSet) = await CodeSetsAsync();
        afterSet.Should().Contain(good.Slug).And.NotContain([noServices.Slug, noProviders.Slug, neither.Slug, hiddenByFlag.Slug]);
        nowSet.Should().NotContain(hiddenByFlag.Slug);
        (await StopReturnsRowAsync()).Should().BeFalse("at least one salon stays visible");

        // Report (psql): lists exactly the disappearing salons with a reason, and not the others.
        var cs = new NpgsqlConnectionStringBuilder(ConnectionString);
        var containerId = await FindContainerIdAsync(cs.Port);
        if (containerId is null)
        {
            Console.WriteLine("[CY49-D1] no docker container publishes the test Postgres port; psql report run skipped.");
            return;
        }

        var report = await File.ReadAllTextAsync(FindDeployCheck("cycle49-catalog-report.sql"));
        var (exit, stdout, stderr) = await RunProcessAsync("docker",
            ["exec", "-i", "-e", $"PGPASSWORD={cs.Password}", containerId, "psql", "-h", "localhost", "-U", cs.Username!, "-d", cs.Database!, "-v", "ON_ERROR_STOP=1"],
            report);

        exit.Should().Be(0, $"psql must run the report without error. stderr: {stderr}");
        stdout.Should().Contain("cycle49-report: done.");
        var disappearing = nowSet.Except(afterSet).ToList();
        foreach (var slug in disappearing) stdout.Should().Contain(slug);
        stdout.Should().NotContain(good.Slug).And.NotContain(hiddenByFlag.Slug);
        stdout.Should().Contain("нет активных услуг и оказывающих услуги").And.Contain("нет оказывающих услуги");
    }

    private static async Task<string?> FindContainerIdAsync(int port)
    {
        try
        {
            var (exit, stdout, _) = await RunProcessAsync("docker", ["ps", "-q", "--filter", $"publish={port}"], null);
            var id = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return exit == 0 ? id : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunProcessAsync(string file, string[] args, string? stdin)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (stdin is not null) await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await p.WaitForExitAsync(cts.Token);
        return (p.ExitCode, await outTask, await errTask);
    }
}
