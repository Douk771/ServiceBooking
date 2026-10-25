using System.Diagnostics;
using FluentAssertions;
using Npgsql;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// CY48-D4 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.15): deploy/checks/cycle48-*.sql run without errors on a freshly migrated schema.
/// The class database is a clone of the migrated template with no seeded data. cycle48-slugs-live.sql is a bare SELECT (Npgsql);
/// cycle48-storefront-report.sql uses psql meta-commands and is run with the container's own psql as deploy-remote.sh does
/// (ON_ERROR_STOP=1). When no docker container publishes the test Postgres port, the psql part reports itself skipped, as LIM19-020b does.
/// </summary>
public class Cycle48ReportSqlTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
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

    [Fact, TestCase("CY48-D4")]
    public async Task SlugsLiveSql_RunsOnAnEmptySchema_AndReturnsNoRows()
    {
        var sql = await File.ReadAllTextAsync(FindDeployCheck("cycle48-slugs-live.sql"));
        sql.Split('\n').Where(l => l.TrimStart().StartsWith('\\')).Should().BeEmpty("the gate file must stay Npgsql-executable");

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = 0;
        while (await reader.ReadAsync()) rows++;

        rows.Should().Be(0, "no non-salon company holds the contacts / payment-info address on a fresh schema");
    }

    [Fact, TestCase("CY48-D4")]
    public async Task StorefrontReportSql_RunsInContainerPsql_OnAnEmptySchema_WithoutError()
    {
        var cs = new NpgsqlConnectionStringBuilder(ConnectionString);
        var containerId = await FindContainerIdAsync(cs.Port);
        if (containerId is null)
        {
            Console.WriteLine("[CY48-D4] no docker container publishes the test Postgres port; psql run skipped (external-server mode or docker CLI unavailable).");
            return;
        }

        var report = await File.ReadAllTextAsync(FindDeployCheck("cycle48-storefront-report.sql"));
        var (exit, stdout, stderr) = await RunProcessAsync("docker",
            ["exec", "-i", "-e", $"PGPASSWORD={cs.Password}", containerId, "psql", "-h", "localhost", "-U", cs.Username!, "-d", cs.Database!, "-v", "ON_ERROR_STOP=1"],
            report);

        exit.Should().Be(0, $"psql must run the report without error. stderr: {stderr}");
        stderr.Should().NotContain("ERROR");
        stdout.Should().Contain("cycle48-report: done.");
        for (var block = 1; block <= 6; block++) stdout.Should().Contain($"cycle48-report [{block}]");
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
