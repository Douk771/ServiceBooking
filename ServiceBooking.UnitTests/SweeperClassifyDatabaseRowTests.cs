using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Покрывает чистую функцию <see cref="Sweeper.ClassifyDatabaseRow"/> — единую логику классификации
/// database-ресурсов, используемую и `sweep`, и `status --json` (review blocker B1), и защиту от сноса
/// живой базы другой машины по ключу прогона (review blocker B2, ARCHITECTURE_CYCLE8.md §70.3).
/// Никакой БД, Docker или реального процесса — только Process.GetProcessById(pid) для собственного PID
/// текущего процесса, который заведомо жив.
/// </summary>
public class SweeperClassifyDatabaseRowTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);
    private const string Workdir = "/repo";

    private static string CommentFor(DateTimeOffset startedAt, string host, int pid, string runKey = "a3f19c7b") =>
        ResourceLabels.ToComment(new ResourceLabels.DatabaseMetadata(runKey, host, pid, startedAt, Workdir));

    [Fact]
    public void Name_not_disposable_is_never_returned_regardless_of_other_fields()
    {
        var result = Sweeper.ClassifyDatabaseRow("servicebooking", comment: null, connections: 0, Now, MaxAge, Workdir, onlyRunKey: null);

        result.Should().BeNull();
    }

    [Fact]
    public void RunKey_filter_excludes_databases_of_other_runs()
    {
        var comment = CommentFor(Now - TimeSpan.FromHours(3), Environment.MachineName, Environment.ProcessId, runKey: "deadbeef");

        var result = Sweeper.ClassifyDatabaseRow("sbtest_deadbeef_api", comment, connections: 0, Now, MaxAge, Workdir, onlyRunKey: "a3f19c7b");

        result.Should().BeNull();
    }

    [Fact]
    public void Old_zero_connections_own_host_dead_pid_is_eligible_for_deletion()
    {
        // metadata.Pid deliberately not a real PID — same-host, provably dead.
        var comment = CommentFor(Now - MaxAge - TimeSpan.FromMinutes(1), Environment.MachineName, pid: 999_999_999);

        var result = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment, connections: 0, Now, MaxAge, Workdir, onlyRunKey: null);

        result.Should().NotBeNull();
        result!.Value.EligibleForDeletion.Should().BeTrue();
        result.Value.Resource.Liveness.Should().Be("dead");
    }

    [Fact]
    public void Open_connections_block_deletion_even_when_old()
    {
        var comment = CommentFor(Now - MaxAge - TimeSpan.FromMinutes(1), Environment.MachineName, pid: 999_999_999);

        var result = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment, connections: 3, Now, MaxAge, Workdir, onlyRunKey: null);

        result!.Value.EligibleForDeletion.Should().BeFalse();
        result.Value.Resource.Liveness.Should().Be("alive");
    }

    [Fact]
    public void Alive_local_process_blocks_deletion_even_when_old_and_run_key_forced()
    {
        var comment = CommentFor(Now - MaxAge - TimeSpan.FromMinutes(1), Environment.MachineName, Environment.ProcessId, runKey: "a3f19c7b");

        var result = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment, connections: 0, Now, MaxAge, Workdir, onlyRunKey: "a3f19c7b");

        result!.Value.EligibleForDeletion.Should().BeFalse();
        result.Value.Resource.Liveness.Should().Be("alive");
    }

    [Fact]
    public void Host_mismatch_pid_evidence_is_unusable_and_never_counts_as_dead()
    {
        // Review blocker B2 scenario: metadata says the database was created on a different machine —
        // IsProcessAlive(metadata.Pid) checking THIS machine's process table is meaningless. Even though
        // the database is old and has zero connections, it must not be classified "dead".
        var comment = CommentFor(Now - MaxAge - TimeSpan.FromMinutes(1), host: "some-other-host", pid: 4242);

        var result = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment, connections: 0, Now, MaxAge, Workdir, onlyRunKey: null);

        result!.Value.EligibleForDeletion.Should().BeFalse();
        result.Value.Resource.Liveness.Should().Be("undetermined");
        result.Value.Resource.HostPidAlive.Should().BeNull();
    }

    [Fact]
    public void Host_mismatch_is_never_deleted_even_when_forced_by_run_key()
    {
        // The core of B2: `sweep --apply --run-key <key from someone else's CI log>` must not be able to
        // drop a live database on a different machine just because its local PID lookup fails.
        var comment = CommentFor(Now - TimeSpan.FromMinutes(1), host: "some-other-host", pid: 4242, runKey: "a3f19c7b");

        var result = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment, connections: 0, Now, MaxAge, Workdir, onlyRunKey: "a3f19c7b");

        result!.Value.EligibleForDeletion.Should().BeFalse();
    }

    [Fact]
    public void Run_key_forcing_does_not_bypass_the_age_gate_for_readable_metadata()
    {
        // Review blocker B2: --run-key used to bypass the age check entirely for ANY resource, not just
        // ones with unreadable metadata. A young, own-host, zero-connection database must stay
        // undetermined (not dead) even when its run key is explicitly named on the command line.
        var comment = CommentFor(Now - TimeSpan.FromMinutes(1), Environment.MachineName, pid: 999_999_999, runKey: "a3f19c7b");

        var result = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment, connections: 0, Now, MaxAge, Workdir, onlyRunKey: "a3f19c7b");

        result!.Value.EligibleForDeletion.Should().BeFalse();
        result.Value.Resource.Liveness.Should().Be("undetermined");
    }

    [Fact]
    public void Unreadable_metadata_is_only_deletable_when_forced_by_run_key()
    {
        var withoutForce = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment: null, connections: 0, Now, MaxAge, Workdir, onlyRunKey: null);
        var withForce = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment: null, connections: 0, Now, MaxAge, Workdir, onlyRunKey: "a3f19c7b");

        withoutForce!.Value.EligibleForDeletion.Should().BeFalse();
        withoutForce.Value.Resource.Liveness.Should().Be("undetermined");

        withForce!.Value.EligibleForDeletion.Should().BeTrue();
        withForce.Value.Resource.Liveness.Should().Be("dead");
    }

    [Fact]
    public void Young_own_host_database_is_undetermined_not_dead()
    {
        var comment = CommentFor(Now - TimeSpan.FromMinutes(1), Environment.MachineName, pid: 999_999_999);

        var result = Sweeper.ClassifyDatabaseRow("sbtest_a3f19c7b_api", comment, connections: 0, Now, MaxAge, Workdir, onlyRunKey: null);

        result!.Value.EligibleForDeletion.Should().BeFalse();
        result.Value.Resource.Liveness.Should().Be("undetermined");
    }
}
