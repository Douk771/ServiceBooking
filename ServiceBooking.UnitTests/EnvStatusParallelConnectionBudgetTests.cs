using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// T8-P9 (ARCHITECTURE_CYCLE8_PHASE2.md §93.4, §99.2) shipped <see cref="EnvStatus.CheckParallelConnectionBudgetAsync"/>-
/// adjacent logic with no unit coverage — backend's own report says the logic was checked only manually,
/// via the CLI and ajv against the JSON schema. This covers the pure arithmetic/parsing pieces that were
/// extracted out for exactly that reason: no Docker, no network, no filesystem beyond a throwaway temp dir.
/// Same pattern as <see cref="EnvStatusClassifyContainerLivenessTests"/> — internal statics reached via
/// InternalsVisibleTo (ServiceBooking.TestKit.csproj).
/// </summary>
public class EnvStatusParallelConnectionBudgetTests
{
    // ── RequiredConnections / SafeConnectionLimit / FitsConnectionBudget — §93.4's formula ──

    [Theory]
    [InlineData(4, 2, 8, 68)]   // 4 * 2 * 8 + 4 = 68 (local default: P=4, hostsPerClass=2, pool=8)
    [InlineData(2, 2, 8, 36)]   // 2 * 2 * 8 + 4 = 36 (CI: P=2)
    [InlineData(1, 1, 1, 5)]
    public void RequiredConnections_matches_the_documented_formula(int maxParallelThreads, int hostsPerClass, int poolSizePerHost, int expected)
    {
        EnvStatus.RequiredConnections(maxParallelThreads, hostsPerClass, poolSizePerHost).Should().Be(expected);
    }

    [Fact]
    public void SafeConnectionLimit_is_ninety_percent_of_server_max_connections()
    {
        EnvStatus.SafeConnectionLimit(300).Should().Be(270);
        EnvStatus.SafeConnectionLimit(100).Should().Be(90);
    }

    [Fact]
    public void FitsConnectionBudget_is_true_exactly_at_the_safe_limit_boundary()
    {
        // 300 * 0.9 = 270 exactly.
        EnvStatus.FitsConnectionBudget(required: 270, serverMaxConnections: 300).Should().BeTrue();
        EnvStatus.FitsConnectionBudget(required: 271, serverMaxConnections: 300).Should().BeFalse();
    }

    [Fact]
    public void FitsConnectionBudget_true_for_local_default_against_container_ceiling_of_300()
    {
        // The exact scenario §93.4 documents: P=4, hostsPerClass=2, pool=8, container max_connections=300.
        var required = EnvStatus.RequiredConnections(maxParallelThreads: 4, hostsPerClass: 2, poolSizePerHost: 8);
        EnvStatus.FitsConnectionBudget(required, serverMaxConnections: 300).Should().BeTrue();
    }

    [Fact]
    public void FitsConnectionBudget_false_when_parallelism_is_pushed_too_high_for_the_server()
    {
        // P=8 against a small external server (safe limit 45) must fail the budget, matching doctor's
        // "reduce parallelism / raise max_connections / drop the external connection" remediation text.
        var required = EnvStatus.RequiredConnections(maxParallelThreads: 8, hostsPerClass: 2, poolSizePerHost: 8);
        EnvStatus.FitsConnectionBudget(required, serverMaxConnections: 50).Should().BeFalse();
    }

    // ── ParseMaxConnectionsFromCommand — the "-c key=value" scan over PostgresCommand ──

    [Fact]
    public void ParseMaxConnectionsFromCommand_reads_the_value_when_present()
    {
        string[] command = ["postgres", "-c", "max_connections=300", "-c", "shared_buffers=256MB"];
        EnvStatus.ParseMaxConnectionsFromCommand(command).Should().Be(300);
    }

    [Fact]
    public void ParseMaxConnectionsFromCommand_is_order_independent()
    {
        string[] command = ["postgres", "-c", "shared_buffers=256MB", "-c", "max_connections=42"];
        EnvStatus.ParseMaxConnectionsFromCommand(command).Should().Be(42);
    }

    [Fact]
    public void ParseMaxConnectionsFromCommand_returns_null_when_flag_absent()
    {
        string[] command = ["postgres", "-c", "shared_buffers=256MB"];
        EnvStatus.ParseMaxConnectionsFromCommand(command).Should().BeNull();
    }

    [Fact]
    public void ParseMaxConnectionsFromCommand_returns_null_on_empty_or_short_command()
    {
        EnvStatus.ParseMaxConnectionsFromCommand([]).Should().BeNull();
        EnvStatus.ParseMaxConnectionsFromCommand(["postgres"]).Should().BeNull();
        EnvStatus.ParseMaxConnectionsFromCommand(["postgres", "-c"]).Should().BeNull(); // dangling flag, no pair
    }

    [Fact]
    public void ParseMaxConnectionsFromCommand_returns_null_for_malformed_pair()
    {
        // No "=", or a non-numeric value: doctor's contract is "cannot determine" (null), not a throw.
        string[] noEquals = ["postgres", "-c", "max_connections"];
        string[] nonNumeric = ["postgres", "-c", "max_connections=not-a-number"];

        EnvStatus.ParseMaxConnectionsFromCommand(noEquals).Should().BeNull();
        EnvStatus.ParseMaxConnectionsFromCommand(nonNumeric).Should().BeNull();
    }

    // ── ResolveMaxParallelThreads — env var override > xunit.runner.json > fallback ──

    [Fact]
    public void ResolveMaxParallelThreads_prefers_the_env_var_override()
    {
        var previous = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS");
        try
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", "7");
            EnvStatus.ResolveMaxParallelThreads(workingCopyRoot: Path.GetTempPath(), fallback: 4).Should().Be(7);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", previous);
        }
    }

    [Fact]
    public void ResolveMaxParallelThreads_ignores_a_non_positive_or_unparsable_env_var_and_falls_through()
    {
        var previous = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS");
        try
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", "0");
            EnvStatus.ResolveMaxParallelThreads(workingCopyRoot: Path.GetTempPath(), fallback: 4).Should().Be(4);

            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", "not-a-number");
            EnvStatus.ResolveMaxParallelThreads(workingCopyRoot: Path.GetTempPath(), fallback: 4).Should().Be(4);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", previous);
        }
    }

    [Fact]
    public void ResolveMaxParallelThreads_reads_xunit_runner_json_when_env_var_is_unset()
    {
        var previous = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS");
        var root = Directory.CreateTempSubdirectory("sb-envstatus-test-").FullName;
        try
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", null);
            var testsDir = Path.Combine(root, "ServiceBooking.Tests");
            Directory.CreateDirectory(testsDir);
            File.WriteAllText(Path.Combine(testsDir, "xunit.runner.json"), """{ "maxParallelThreads": 6 }""");

            EnvStatus.ResolveMaxParallelThreads(root, fallback: 4).Should().Be(6);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveMaxParallelThreads_falls_back_when_no_env_var_and_no_runner_json()
    {
        var previous = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS");
        var root = Directory.CreateTempSubdirectory("sb-envstatus-test-").FullName;
        try
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", null);
            EnvStatus.ResolveMaxParallelThreads(root, fallback: 4).Should().Be(4);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveMaxParallelThreads_falls_back_on_malformed_runner_json()
    {
        var previous = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS");
        var root = Directory.CreateTempSubdirectory("sb-envstatus-test-").FullName;
        try
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", null);
            var testsDir = Path.Combine(root, "ServiceBooking.Tests");
            Directory.CreateDirectory(testsDir);
            File.WriteAllText(Path.Combine(testsDir, "xunit.runner.json"), "{ not valid json");

            EnvStatus.ResolveMaxParallelThreads(root, fallback: 4).Should().Be(4);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS", previous);
            Directory.Delete(root, recursive: true);
        }
    }
}
