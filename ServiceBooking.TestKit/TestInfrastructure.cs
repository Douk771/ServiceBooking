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
    /// see ARCHITECTURE_CYCLE8.md §75.</summary>
    public static readonly string[] PostgresCommand =
    [
        "-c", "max_connections=200",
        "-c", "fsync=off",
        "-c", "full_page_writes=off",
        "-c", "synchronous_commit=off",
        "-c", "shared_buffers=128MB",
    ];

    /// <summary>Npgsql connection-string tail applied by <see cref="TestDatabaseLease"/> so a
    /// handful of short-lived WebApplicationFactory hosts can't exhaust the server's connection
    /// budget — see ARCHITECTURE_CYCLE8.md §75.</summary>
    public const string PoolLimitTail = "Maximum Pool Size=15;Connection Idle Lifetime=10;Timeout=15";
}
