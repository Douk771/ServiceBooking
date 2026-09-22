using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.TestKit;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Owns the one <see cref="TestServerLease"/> and <see cref="TestDatabaseLease"/> for this test process
/// (ARCHITECTURE_CYCLE8.md §66/§70.1). Ref-counted across the three collection fixtures
/// (<see cref="ApiDatabaseFixture"/>, <see cref="LegalDatabaseFixture"/>, <see cref="DispatchDatabaseFixture"/>)
/// so the server/databases are created once, lazily, on whichever fixture initializes first, and torn
/// down once, when the last of the three finishes — regardless of which xUnit collection runs first or
/// last within the process.
/// </summary>
public static class TestRunEnvironment
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static TestServerLease? _server;
    private static TestDatabaseLease? _databases;
    private static int _refCount;
    private static bool _bannerPrinted;

    /// <summary>Registers one more owner of the shared environment and returns the (already fully
    /// provisioned — all three slot databases exist) lease. Call exactly once per fixture, from
    /// <see cref="IAsyncLifetime.InitializeAsync"/>.</summary>
    public static async Task<TestDatabaseLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            _refCount++;

            if (_databases is null)
            {
                _server = await TestServerLease.AcquireAsync(cancellationToken);
                _databases = new TestDatabaseLease(_server);
                await _databases.EnsureSlotsAsync(TestSlot.All, MigrateTemplateAsync, cancellationToken);
                PrintBanner(_server);
            }

            return _databases;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Releases one owner's hold on the shared environment. Tears the server/databases down once
    /// the last owner releases — <see cref="TestServerMode.Container"/> just disposes the container
    /// (which throws every database away for free); <see cref="TestServerMode.External"/> drops each
    /// slot's database explicitly via <see cref="TestDatabaseLease.DropAllAsync"/> (§70.1).</summary>
    public static async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_refCount == 0)
                return; // defensive — a fixture whose InitializeAsync never ran must not decrement below zero.

            _refCount--;
            if (_refCount > 0)
                return;

            if (_server is { Mode: TestServerMode.External } && _databases is not null)
                await _databases.DropAllAsync(cancellationToken);

            if (_server is not null)
                await _server.DisposeAsync();

            _server = null;
            _databases = null;
            _bannerPrinted = false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task MigrateTemplateAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
    }

    /// <summary>First line of test output (ARCHITECTURE_CYCLE8.md §67) plus a machine-readable twin for
    /// CI artifacts/QA (<c>TestResults/sb-test-run.json</c>).</summary>
    private static void PrintBanner(TestServerLease server)
    {
        if (_bannerPrinted)
            return;
        _bannerPrinted = true;

        var mode = server.Mode == TestServerMode.Container ? "container" : "external";
        var databases = string.Join(",", TestSlot.All);
        var line = $"[sb-test] run={TestRunKey.Current}  mode={mode}  server={server.Host}:{server.Port}  " +
                   $"databases=sbtest_{TestRunKey.Current}_{{{databases}}}";
        Console.WriteLine(line);

        try
        {
            var resultsDirectory = Path.Combine(TestInfrastructure.WorkingCopyRoot, "TestResults");
            Directory.CreateDirectory(resultsDirectory);
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                runKey = TestRunKey.Current,
                mode,
                server = $"{server.Host}:{server.Port}",
                databases = TestSlot.All.Select(slot => $"sbtest_{TestRunKey.Current}_{slot}").ToArray(),
            }, TestKitJson.Options);
            File.WriteAllText(Path.Combine(resultsDirectory, "sb-test-run.json"), json);
        }
        catch (IOException)
        {
            // Best-effort artifact — a write failure here must never fail the test run itself.
        }
    }
}
