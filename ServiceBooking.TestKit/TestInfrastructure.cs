namespace ServiceBooking.TestKit;

/// <summary>
/// Values that must agree across every place that talks about the test/dev Postgres — see
/// ARCHITECTURE_CYCLE8.md §74.3. This class is the authoritative source; docker-compose.yml,
/// docker-compose.prod.yml and .github/workflows/ci.yml repeat the major version, and
/// deploy/ci/check-image-pins.sh fails the build if they drift apart.
/// </summary>
public static class TestInfrastructure
{
    /// <summary>Image used by Testcontainers to boot the ephemeral test server (§68).</summary>
    public const string PostgresImage = "postgres:16-alpine";

    /// <summary>Major version repeated (not copy-pasted verbatim) by docker-compose.yml,
    /// docker-compose.prod.yml and ci.yml's service container. Kept as an int so
    /// check-image-pins.sh can compare "16" against "16-alpine" without string gymnastics.</summary>
    public const int PostgresMajorVersion = 16;

    /// <summary>Default TTL used by the sweeper (§70.3) when --max-age is not passed.</summary>
    public static readonly TimeSpan DefaultSweepMaxAge = TimeSpan.FromHours(2);

    /// <summary>Command-line flags applied to the ephemeral Testcontainers Postgres instance.
    /// All three "off" settings are safe here because the data is single-run and disposable —
    /// see ARCHITECTURE_CYCLE8.md §75. max_connections raised 200→300 and max_locks_per_transaction
    /// added in ARCHITECTURE_CYCLE8_PHASE2.md §93.3 (T8-P8): class-per-database parallelism (P=4) peaks
    /// at 65 connections (§93.2) — 300 keeps a ~4.6x margin — and concurrent CREATE DATABASE ... TEMPLATE
    /// / DROP DATABASE across parallel classes takes noticeably more locks than the old single-threaded
    /// run.</summary>
    public static readonly string[] PostgresCommand =
    [
        "-c", "max_connections=300",
        "-c", "fsync=off",
        "-c", "full_page_writes=off",
        "-c", "synchronous_commit=off",
        "-c", "shared_buffers=192MB",
        "-c", "max_locks_per_transaction=128",
    ];

    /// <summary>Pool limit applied by <see cref="TestDatabaseLease"/>, via
    /// <see cref="Npgsql.NpgsqlConnectionStringBuilder"/> properties (not string concatenation — a
    /// concatenated tail can silently duplicate keys already present in an operator-supplied
    /// SERVICEBOOKING_TEST_CONNECTION), so a handful of short-lived WebApplicationFactory hosts can't
    /// exhaust the server's connection budget — see ARCHITECTURE_CYCLE8.md §75. Lowered 15→8 in
    /// ARCHITECTURE_CYCLE8_PHASE2.md §93.2 (T8-P8): with one class owning one host, and tests inside a
    /// class strictly sequential (§92.1), a single host never physically holds more than 2-3 concurrent
    /// connections — 8 is already generous, and the lower number is what makes the P=4 connection budget
    /// (65 of 300) fit comfortably.
    ///
    /// T9 review (M4) — this is a ceiling PER CLASS, not per host: Npgsql pools are keyed by connection
    /// string, and every host inside one test class (the class fixture's own, plus any short-lived
    /// per-test factory — §92.4) is built with the SAME connection string, so they draw from one shared
    /// pool of 8, not one each. That is safe for the server (the budget formula in
    /// <see cref="EnvStatus.RequiredConnections"/> multiplies by hostsPerClass anyway, as a margin — see
    /// its own note), but "8 per host" would be the wrong mental model if a future change made per-class
    /// concurrency less strictly sequential than §92.1 currently guarantees.</summary>
    public const int PoolMaxSize = 8;
    public const int PoolConnectionIdleLifetimeSeconds = 10;
    public const int PoolTimeoutSeconds = 15;

    /// <summary>Resolves the working-copy root (directory containing ServiceBooking.sln) by walking up
    /// from <see cref="Environment.CurrentDirectory"/>. Both the resource-creating side (test host,
    /// running from a bin/Debug/net8.0 subfolder) and the resource-reading side (TestKit CLI, run from
    /// the repo root) must agree on this value, or the "mine vs. someone else's" comparison in
    /// EnvStatus/Sweeper is meaningless — see non-blocking review finding on TestServerLease.cs:82.</summary>
    public static string WorkingCopyRoot
    {
        get
        {
            var directory = new DirectoryInfo(Environment.CurrentDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ServiceBooking.sln")) ||
                    Directory.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return Environment.CurrentDirectory;
        }
    }
}
