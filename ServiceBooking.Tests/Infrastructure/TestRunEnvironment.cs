using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.TestKit;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// One class database, leased from <see cref="TestRunEnvironment"/> for the lifetime of one
/// <see cref="TestDatabaseFixture"/> (ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.2). <see cref="DropAsync"/>
/// drops the class database and releases this lease's hold on the shared server/template — call exactly
/// once, from the owning fixture's <see cref="IAsyncLifetime.DisposeAsync"/>.
/// </summary>
public sealed class TestClassDatabaseLease(string classSlot, string connectionString)
{
    public string ClassSlot { get; } = classSlot;
    public string ConnectionString { get; } = connectionString;

    private int _dropped;

    public async Task DropAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _dropped, 1) == 1)
            return; // defensive — DropAsync must be idempotent if a fixture's DisposeAsync is ever retried.

        await TestRunEnvironment.ReleaseClassDatabaseAsync(ClassSlot, cancellationToken);
    }

    /// <summary>T9 review (M3): annotates this class' database with the owning test class' name, once it
    /// is known (see <see cref="TestDatabaseFixture.RecordTestClass"/> for why this can't happen any
    /// earlier). Best-effort diagnostics only — never throws into the caller.</summary>
    public async Task RecordTestClassAsync(string testClassName, CancellationToken cancellationToken = default)
    {
        try
        {
            await TestRunEnvironment.RecordTestClassAsync(ClassSlot, testClassName, cancellationToken);
        }
        catch
        {
            // Diagnostics only — a failure here must never fail the test run.
        }
    }
}

/// <summary>
/// Owns the one <see cref="TestServerLease"/> and <see cref="TestDatabaseLease"/> for this test PROCESS
/// (ARCHITECTURE_CYCLE8.md §66/§70.1) — created lazily by whichever <see cref="TestDatabaseFixture"/>
/// (one per test class, ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.2) initializes first, and kept alive for
/// the rest of the process, not ref-counted down to zero between classes.
///
/// Phase 1's ref-count-to-zero teardown (still visible in git history) was written for exactly THREE
/// long-lived collection fixtures whose lifetimes overlapped for the whole run. A per-class fixture
/// breaks that assumption: with <c>[assembly: CollectionBehavior(DisableTestParallelization = true)]</c>
/// still in force (§98.1 — flipping it is T8-P7, a separate step), xUnit runs test classes one at a time,
/// so a class' <c>TestDatabaseFixture.DisposeAsync</c> is the SOLE live owner at the moment it releases —
/// ref-counting to zero there would tear the container down and rebuild it before every single class
/// (confirmed empirically: the very first version of this file did exactly that, ~10s × 27 classes).
/// The container is intended to live for the whole run (§66) regardless of unit of parallelism, so
/// teardown here is tied to the PROCESS via <see cref="AppDomain.ProcessExit"/> instead of to the last
/// class finishing. <see cref="ReleaseClassDatabaseAsync"/> still drops each class' own database as that
/// class finishes (§91.4 — at most P+1 live databases at any moment), it just no longer drops the server.
/// </summary>
public static class TestRunEnvironment
{
    // L2 (T9 review): this used to be one semaphore for two unrelated purposes -- standing up the
    // shared server/template (EnsureEnvironmentAsync, needed once, briefly, at the very start of the
    // run) and dropping one class' database (ReleaseClassDatabaseAsync, happening constantly throughout
    // the run as classes finish). A slow DROP DATABASE for one finishing class held the same lock a
    // brand-new class' InitializeAsync needed just to read `_databases` back out of already-initialized
    // state, serializing "start next class" behind "tear down previous class" for no reason. Split so
    // the two can proceed concurrently.
    private static readonly SemaphoreSlim EnvironmentGate = new(1, 1);
    private static readonly SemaphoreSlim DropGate = new(1, 1);

    private static TestServerLease? _server;
    private static TestDatabaseLease? _databases;
    private static bool _bannerPrinted;
    private static bool _teardownRegistered;

    // L3 (T9 review): EnsureConnectionBudgetAsync used to run once per LEASED CLASS (~29 extra
    // connections + SHOW max_connections round trips over a run) even though the answer -- parallelism,
    // pool size and the server's max_connections -- cannot change mid-run. Checked once per process.
    private static bool _connectionBudgetChecked;

    /// <summary>Ensures the server/template exist (creating them on the very first call across the whole
    /// process), then clones and returns this class' own database. Call exactly once per
    /// <see cref="TestDatabaseFixture"/>, from <see cref="IAsyncLifetime.InitializeAsync"/>.</summary>
    public static async Task<TestClassDatabaseLease> LeaseClassDatabaseAsync(string classSlot, CancellationToken cancellationToken = default)
    {
        var databases = await EnsureEnvironmentAsync(cancellationToken);

        // §93.4 (T8-P9): fail fast, before this class' database (let alone any test) exists, if the
        // configured parallelism would ask for more connections than the server can safely hand out —
        // "connection limit exceeded" 300 tests into a run is expensive to diagnose; this is not.
        await EnsureConnectionBudgetCheckedOnceAsync(cancellationToken);

        var databaseName = await databases.CreateClassDatabaseAsync(classSlot, cancellationToken);
        var connectionString = databases.ConnectionStringFor(classSlot);
        Console.WriteLine($"[sb-test] class-db slot={classSlot} db={databaseName}");
        return new TestClassDatabaseLease(classSlot, connectionString);
    }

    /// <summary>Drops one class' own database — the server/template outlive it, torn down once, at
    /// process exit (see this type's own doc comment). Called only through
    /// <see cref="TestClassDatabaseLease.DropAsync"/>. Guarded by <see cref="DropGate"/>, not
    /// <see cref="EnvironmentGate"/> (L2) — a slow DROP for one finishing class must never block another
    /// class' <see cref="LeaseClassDatabaseAsync"/> from starting.</summary>
    internal static async Task ReleaseClassDatabaseAsync(string classSlot, CancellationToken cancellationToken = default)
    {
        await DropGate.WaitAsync(cancellationToken);
        try
        {
            if (_databases is not null)
                await _databases.DropClassDatabaseAsync(classSlot, cancellationToken);
        }
        finally
        {
            DropGate.Release();
        }
    }

    /// <summary>T9 review (M3) — see <see cref="TestClassDatabaseLease.RecordTestClassAsync"/>. Not gated
    /// by either semaphore: this only writes a COMMENT ON DATABASE for a class database that is (by
    /// construction — the caller is that class' own base-class constructor) still alive right now, and it
    /// races with nothing that would corrupt state if it ran concurrently with a drop (worst case: the
    /// comment update targets an already-gone database and fails, caught above).</summary>
    internal static async Task RecordTestClassAsync(string classSlot, string testClassName, CancellationToken cancellationToken)
    {
        if (_databases is not null)
            await _databases.RecordTestClassAsync(classSlot, testClassName, cancellationToken);
    }

    private static async Task<TestDatabaseLease> EnsureEnvironmentAsync(CancellationToken cancellationToken)
    {
        await EnvironmentGate.WaitAsync(cancellationToken);
        try
        {
            if (_databases is null)
            {
                try
                {
                    _server = await TestServerLease.AcquireAsync(cancellationToken);
                    _databases = new TestDatabaseLease(_server);
                    await _databases.EnsureTemplateAsync(MigrateTemplateAsync, cancellationToken);
                    PrintBanner(_server);
                    RegisterProcessExitTeardown();
                }
                catch
                {
                    // Partial initialization (e.g. template migration failed) must not leave a live
                    // container behind for the next class to inherit -- xUnit never calls DisposeAsync on
                    // a fixture whose InitializeAsync threw, so this is the only chance to roll everything
                    // back before the exception propagates.
                    if (_server is not null)
                        await _server.DisposeAsync();

                    _server = null;
                    _databases = null;
                    throw;
                }
            }

            return _databases;
        }
        finally
        {
            EnvironmentGate.Release();
        }
    }

    /// <summary>Tears the server down exactly once, when this test PROCESS exits — not when the last
    /// class-scoped lease releases (see this type's own doc comment for why). <see cref="TestServerMode.Container"/>
    /// disposing the container throws every database away for free; <see cref="TestServerMode.External"/>
    /// drops the template explicitly (§70.1) — every class database was already dropped individually by
    /// <see cref="ReleaseClassDatabaseAsync"/> as its class finished. Best-effort: if the process is
    /// killed instead of exiting normally, Ryuk (§70) and the sweeper (§70.3) are the safety net this was
    /// always designed to have.</summary>
    private static void RegisterProcessExitTeardown()
    {
        if (_teardownRegistered)
            return;
        _teardownRegistered = true;

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (_server is { Mode: TestServerMode.External } && _databases is not null)
                    _databases.DropAllAsync().GetAwaiter().GetResult();

                _server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort at process exit — Ryuk/the sweeper clean up anything left behind (§70).
            }
        };
    }

    /// <summary>ARCHITECTURE_CYCLE8_PHASE2.md §93.4 (T8-P9) — the same arithmetic the architecture
    /// prescribes for <c>doctor</c>'s <c>parallel-connection-budget</c> check, run here so a budget that
    /// does not add up fails the whole run before the first test, with an actionable message, rather than
    /// as a random "connection limit exceeded" wherever the pool finally runs dry.
    ///
    /// T9 review (L3): the answer to "does the budget fit" cannot change mid-run — parallelism, pool size
    /// per class and the server's max_connections are all fixed once the process starts — so this used to
    /// redo the SHOW max_connections round trip and the arithmetic for every one of ~29 classes for no
    /// reason. Runs at most once per process now; every class after the first just observes the cached
    /// verdict for free.</summary>
    private static async Task EnsureConnectionBudgetCheckedOnceAsync(CancellationToken cancellationToken)
    {
        if (_connectionBudgetChecked || _server is null)
            return;

        // Not gated by EnvironmentGate/DropGate — a second class racing in here before the flag is set
        // would just redo the same read-only check and get the same answer (or the same exception) a
        // second time; harmless, and not worth a third semaphore.
        _connectionBudgetChecked = true;

        var maxParallelThreads = TestParallelism.MaxParallelThreads;
        const int hostsPerClass = 2; // §92.4 / EnvStatus.CheckParallelConnectionBudgetAsync's own note (T9 M4).

        // T9 review (M4): was `maxParallelThreads * 2 * TestInfrastructure.PoolMaxSize + 4` typed out here
        // a second time, next to an identical literal in EnvStatus.RequiredConnections — despite both
        // copies' comments claiming "no number duplicated". Now the one formula EnvStatus exposes.
        var required = EnvStatus.RequiredConnections(maxParallelThreads, hostsPerClass, TestInfrastructure.PoolMaxSize);

        await using var connection = new Npgsql.NpgsqlConnection(_server.MaintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand("SHOW max_connections", connection);
        var maxConnectionsRaw = (string?)await command.ExecuteScalarAsync(cancellationToken);

        // T9 review (L3): int.Parse on a value this process doesn't control (whatever the connected
        // Postgres server reports back) used to throw FormatException — an unhandled crash with no
        // actionable message — instead of the documented TestSafetyException path if the server ever
        // answered with something unparsable.
        if (maxConnectionsRaw is null || !int.TryParse(maxConnectionsRaw, out var maxConnections))
        {
            throw new TestSafetyException(
                $"[sb-test] Отказ: сервер вернул нечисловой max_connections (\"{maxConnectionsRaw}\") — " +
                "бюджет соединений проверить не удалось. Ничего не создано и не удалено.");
        }

        var safeLimit = EnvStatus.SafeConnectionLimit(maxConnections);
        if (EnvStatus.FitsConnectionBudget(required, maxConnections))
            return;

        throw new TestSafetyException(
            "[sb-test] Отказ: бюджет соединений не сходится.\n" +
            $"  Параллелизм P={maxParallelThreads}, пул на хост={TestInfrastructure.PoolMaxSize}, хостов на класс=2 " +
            $"→ нужно {required} соединений.\n" +
            $"  Сервер отдаёт max_connections={maxConnections}, безопасный предел {safeLimit:F0}.\n" +
            "  Что сделать (любое из):\n" +
            "    1) снизить параллелизм — ДВА места должны совпадать: SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS=2\n" +
            "       (эта переменная влияет только на арифметику этой проверки) И фактический параллелизм раннера,\n" +
            "       который задают отдельно: `-- xUnit.MaxParallelThreads=2` в командной строке dotnet test либо\n" +
            "       maxParallelThreads в ServiceBooking.Tests/xunit.runner.json. Если поменять только переменную,\n" +
            "       раннер по-прежнему будет параллелить на старом значении — либо тот же отказ, либо (хуже) эта\n" +
            "       проверка пройдёт, а лимит соединений вылезет посреди прогона.\n" +
            "    2) поднять потолок сервера: max_connections >= " + (int)Math.Ceiling(required / 0.9) + "\n" +
            "    3) убрать SERVICEBOOKING_TEST_CONNECTION и дать прогону поднять свой контейнер\n" +
            "  Ничего не создано и не удалено.");
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
        var user = new Npgsql.NpgsqlConnectionStringBuilder(server.MaintenanceConnectionString).Username ?? "?";
        var line = $"[sb-test] run={TestRunKey.Current}  mode={mode}  server={server.Host}:{server.Port}  " +
                   $"user={user}  databases=sbtest_{TestRunKey.Current}_<class-slot>  " +
                   $"parallel={TestParallelism.MaxParallelThreads}";
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
                user,
                maxParallelThreads = TestParallelism.MaxParallelThreads,
            }, TestKitJson.Options);
            File.WriteAllText(Path.Combine(resultsDirectory, "sb-test-run.json"), json);
        }
        catch (IOException)
        {
            // Best-effort artifact — a write failure here must never fail the test run itself.
        }
    }
}
