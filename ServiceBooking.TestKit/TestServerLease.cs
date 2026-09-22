using Npgsql;
using Testcontainers.PostgreSql;

namespace ServiceBooking.TestKit;

/// <summary>Which way the current run obtained its Postgres server — printed as the first line of
/// output per ARCHITECTURE_CYCLE8.md §67, and used by <see cref="TestDatabaseLease"/> to decide
/// whether tearing down means "drop the databases" or "throw the whole container away".</summary>
public enum TestServerMode
{
    Container,
    External,
}

/// <summary>
/// Resolves and owns the Postgres server a test run talks to — one per process (§66, §69.1).
/// <list type="bullet">
/// <item><description><c>SERVICEBOOKING_TEST_CONNECTION</c> set → <see cref="TestServerMode.External"/>,
/// no container is started (keeps the CI service-container path working, US-84).</description></item>
/// <item><description>not set, Docker reachable → <see cref="TestServerMode.Container"/>, Testcontainers
/// boots <see cref="TestInfrastructure.PostgresImage"/> on a random host port.</description></item>
/// <item><description>not set, Docker unreachable → throws <see cref="TestSafetyException"/> with a
/// fail-fast message; no destructive action is ever taken as a fallback (§69.1).</description></item>
/// </list>
/// </summary>
public sealed class TestServerLease : IAsyncDisposable
{
    private const string ConnectionEnvironmentVariable = "SERVICEBOOKING_TEST_CONNECTION";

    private PostgreSqlContainer? _container;

    public TestServerMode Mode { get; }

    /// <summary>Connection string to the server's maintenance database ("postgres"), suitable for
    /// issuing CREATE DATABASE / DROP DATABASE. Never points at a database name a caller chose.</summary>
    public string MaintenanceConnectionString { get; }

    public string Host { get; }
    public int Port { get; }

    private TestServerLease(TestServerMode mode, PostgreSqlContainer? container, string maintenanceConnectionString, string host, int port)
    {
        Mode = mode;
        _container = container;
        MaintenanceConnectionString = maintenanceConnectionString;
        Host = host;
        Port = port;
    }

    public static async Task<TestServerLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var external = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(external))
            return AcquireExternal(external);

        return await AcquireContainerAsync(cancellationToken);
    }

    private static TestServerLease AcquireExternal(string connectionString)
    {
        // The variable is a SERVER connection string as of cycle 8 (§69.2) — the database component
        // is not the caller's to choose, so it is always overwritten with the maintenance database.
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        return new TestServerLease(TestServerMode.External, null, builder.ConnectionString, builder.Host ?? "localhost", builder.Port);
    }

    private static async Task<TestServerLease> AcquireContainerAsync(CancellationToken cancellationToken)
    {
        PostgreSqlContainer container;
        try
        {
            container = new PostgreSqlBuilder()
                .WithImage(TestInfrastructure.PostgresImage)
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCommand(TestInfrastructure.PostgresCommand)
                .WithLabel(ResourceLabels.OwnerLabel, "1")
                .WithLabel(ResourceLabels.RunKeyLabel, TestRunKey.Current)
                .WithLabel(ResourceLabels.HostPidLabel, Environment.ProcessId.ToString())
                .WithLabel(ResourceLabels.StartedAtLabel, DateTimeOffset.UtcNow.ToString("O"))
                .WithLabel(ResourceLabels.WorkdirLabel, Environment.CurrentDirectory)
                .Build();

            await container.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new TestSafetyException(
                "[sb-test] Не могу подготовить базу для прогона.\n" +
                $"  Docker недоступен: {ex.Message}\n" +
                "  Варианты:\n" +
                "    1) запустить Docker Desktop и повторить — это штатный путь;\n" +
                "    2) указать свой сервер PostgreSQL:\n" +
                "       SERVICEBOOKING_TEST_CONNECTION=\"Host=localhost;Port=5432;Username=postgres;Password=...\"\n" +
                "       (имя базы в строке игнорируется, прогон создаёт свои базы sbtest_<ключ>_<слот>)\n" +
                "  Подробности: docs/testing-isolation.md",
                ex);
        }

        var maintenanceConnectionString = container.GetConnectionString();
        var host = container.Hostname;
        var port = container.GetMappedPublicPort(5432);

        return new TestServerLease(TestServerMode.Container, container, maintenanceConnectionString, host, port);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }
}
