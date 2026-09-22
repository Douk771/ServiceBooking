using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace ServiceBooking.TestKit;

/// <summary>
/// Third line of defence for leaked test resources (§70.3): dry-run by default, requires --apply to
/// actually delete anything, and never touches a resource it can't classify as dead with confidence.
/// Машиночитаемый вывод (--json) обязан соответствовать contracts/cycle8/testkit-status.schema.json
/// (§86.3): только JSON в stdout, человекочитаемое — в stderr.
/// </summary>
public static class Sweeper
{
    public static async Task<int> RunAsync(string[] args)
    {
        var asJson = args.Contains("--json");
        var apply = args.Contains("--apply");
        var maxAge = TestInfrastructure.DefaultSweepMaxAge;
        string? onlyRunKey;
        try
        {
            var maxAgeArgIndex = Array.IndexOf(args, "--max-age");
            if (maxAgeArgIndex >= 0 && maxAgeArgIndex + 1 < args.Length)
                maxAge = ParseAge(args[maxAgeArgIndex + 1]);

            var onlyRunKeyIndex = Array.IndexOf(args, "--run-key");
            onlyRunKey = onlyRunKeyIndex >= 0 && onlyRunKeyIndex + 1 < args.Length
                ? TestRunKey.Normalize(args[onlyRunKeyIndex + 1])
                : null;
        }
        catch (TestSafetyException ex)
        {
            // §86.2: exit code 1 is "некорректные аргументы" — must not surface as an unhandled
            // exception/stack trace (review finding: 'sweep --max-age 90s' used to crash the process).
            Console.Error.WriteLine("[sb-sweep] " + ex.Message);
            return 1;
        }

        var now = DateTimeOffset.UtcNow;
        var workingCopyRoot = TestInfrastructure.WorkingCopyRoot;

        var dead = new List<TestResource>();
        var alive = new List<TestResource>();
        var undetermined = new List<TestResource>();
        var removed = new List<TestResource>();
        var errors = new List<SweepError>();

        var dockerAvailable = await IsDockerAvailableAsync();
        var dockerInfo = new DockerInfo(dockerAvailable, dockerAvailable ? null : "docker недоступен, контейнеры пропущены");

        if (dockerAvailable)
            await SweepContainersAsync(workingCopyRoot, now, maxAge, onlyRunKey, apply, dead, alive, undetermined, removed, errors);

        var externalConnection = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(externalConnection))
            await SweepDatabasesAsync(externalConnection, workingCopyRoot, now, maxAge, onlyRunKey, apply, dead, alive, undetermined, removed, errors);

        var exitCode = apply && errors.Count > 0 ? 3 : 0;

        if (asJson)
        {
            var document = new SweepDocument(
                Command: "sweep",
                SchemaVersion: TestKitJson.SchemaVersion,
                GeneratedAtUtc: TestKitJson.ToIso8601(DateTimeOffset.UtcNow),
                ExitCode: exitCode,
                Applied: apply,
                MaxAgeSeconds: (int)maxAge.TotalSeconds,
                RunKeyFilter: onlyRunKey,
                Docker: dockerInfo,
                Dead: dead.ToArray(),
                Alive: alive.ToArray(),
                Undetermined: undetermined.ToArray(),
                Removed: removed.ToArray(),
                Errors: errors.ToArray());

            TestKitJson.WriteJson(document);
        }
        else
        {
            if (dead.Count > 0)
            {
                Console.Error.WriteLine(apply ? "[sb-sweep] Удалены:" : "[sb-sweep] Мёртвые (будут удалены с --apply):");
                foreach (var r in dead) Console.Error.WriteLine("  " + Describe(r));
            }

            if (alive.Count > 0)
            {
                Console.Error.WriteLine("[sb-sweep] Живые (не трогаю):");
                foreach (var r in alive) Console.Error.WriteLine("  " + Describe(r));
            }

            if (undetermined.Count > 0)
            {
                Console.Error.WriteLine("[sb-sweep] Неопределённые (не трогаю, проверьте руками):");
                foreach (var r in undetermined) Console.Error.WriteLine("  " + Describe(r));
            }

            if (dead.Count == 0 && alive.Count == 0 && undetermined.Count == 0)
                Console.Error.WriteLine("[sb-sweep] Ничего не найдено.");

            foreach (var e in errors)
                Console.Error.WriteLine("[sb-sweep] ОШИБКА: " + e.Message);
        }

        return exitCode;
    }

    private static string Describe(TestResource r) =>
        r.Kind == "container"
            ? $"container {r.Id}  age={FormatAge(r.AgeSeconds)}  pid={r.HostPid}({(r.HostPidAlive == true ? "жив" : "нет")})"
            : $"database  {r.Id}  age={FormatAge(r.AgeSeconds)}  conns={r.Connections}";

    internal static TimeSpan ParseAge(string value)
    {
        // "30m", "2h", "1d" — §86.1 documents all three units.
        if (value.EndsWith('m') && int.TryParse(value[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);
        if (value.EndsWith('h') && int.TryParse(value[..^1], out var hours))
            return TimeSpan.FromHours(hours);
        if (value.EndsWith('d') && int.TryParse(value[..^1], out var days))
            return TimeSpan.FromDays(days);
        throw new TestSafetyException($"[sb-sweep] Не понимаю --max-age \"{value}\". Ожидался формат вроде 30m, 2h или 1d.");
    }

    private static async Task SweepContainersAsync(string workingCopyRoot, DateTimeOffset now, TimeSpan maxAge, string? onlyRunKey, bool apply,
        List<TestResource> dead, List<TestResource> alive, List<TestResource> undetermined, List<TestResource> removed, List<SweepError> errors)
    {
        string psOutput;
        try
        {
            psOutput = await RunDockerAsync(["ps", "-a", "--filter", $"label={ResourceLabels.OwnerLabel}=1", "--format", "{{json .}}"]);
        }
        catch (Exception ex)
        {
            errors.Add(new SweepError(null, $"docker недоступен, контейнеры пропущены: {ex.Message}"));
            return;
        }

        foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var id = doc.RootElement.GetProperty("ID").GetString()!;
            var name = doc.RootElement.GetProperty("Names").GetString() ?? id;

            string inspect;
            try
            {
                inspect = await RunDockerAsync($"inspect {id}");
            }
            catch (Exception)
            {
                // Race between `docker ps` and `docker inspect`: the container (often Ryuk) can be gone
                // by the time we inspect it. That's not a sweep failure — the container is already gone,
                // which is the outcome sweep would have produced anyway — so skip it rather than aborting
                // the whole command (previous behaviour) or reporting it via errors[], which §86.2 ties
                // specifically to "part of the resources could not be deleted".
                continue;
            }

            using var inspectDoc = JsonDocument.Parse(inspect);
            var labels = inspectDoc.RootElement[0].GetProperty("Config").GetProperty("Labels");

            var runKey = labels.TryGetProperty(ResourceLabels.RunKeyLabel, out var rk) ? rk.GetString() : null;
            if (runKey is null)
                continue;
            if (onlyRunKey is not null && runKey != onlyRunKey)
                continue;

            var workdir = labels.TryGetProperty(ResourceLabels.WorkdirLabel, out var wd) ? wd.GetString() : null;
            var hostPidRaw = labels.TryGetProperty(ResourceLabels.HostPidLabel, out var pidProp) ? pidProp.GetString() : null;
            var startedAtRaw = labels.TryGetProperty(ResourceLabels.StartedAtLabel, out var saProp) ? saProp.GetString() : null;

            var hostPid = int.TryParse(hostPidRaw, out var pid) ? (int?)pid : null;
            var processAlive = hostPid is not null && IsProcessAlive(hostPid.Value);
            var startedAt = startedAtRaw is not null && DateTimeOffset.TryParse(startedAtRaw, out var started) ? started : now;
            var ageSeconds = (int)Math.Max(0, (now - startedAt).TotalSeconds);
            var mine = workdir == workingCopyRoot;

            var resource = new TestResource(
                Kind: "container",
                Id: name,
                RunKey: runKey,
                Slot: null,
                TestClass: null,
                Workdir: workdir,
                StartedAtUtc: TestKitJson.ToIso8601(startedAt),
                AgeSeconds: ageSeconds,
                HostPid: hostPid,
                HostPidAlive: processAlive,
                Connections: null,
                Mine: mine,
                Liveness: "undetermined");

            // §86.1: --run-key is the only way to remove an "undetermined" resource — the operator is
            // vouching for a specific run by key, so the normal age/liveness gate is bypassed for it.
            var forcedByRunKey = onlyRunKey is not null;

            if (!processAlive && (ageSeconds > maxAge.TotalSeconds || forcedByRunKey))
            {
                dead.Add(resource with { Liveness = "dead" });
                if (apply)
                {
                    try
                    {
                        await RunDockerAsync($"rm -f {id}");
                        removed.Add(resource with { Liveness = "dead" });
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new SweepError(name, $"не удалось удалить контейнер {name}: {ex.Message}"));
                    }
                }
            }
            else if (processAlive)
            {
                alive.Add(resource with { Liveness = "alive" });
            }
            else
            {
                undetermined.Add(resource with { Liveness = "undetermined" });
            }
        }
    }

    private static async Task SweepDatabasesAsync(string serverConnectionString, string workingCopyRoot, DateTimeOffset now, TimeSpan maxAge, string? onlyRunKey,
        bool apply, List<TestResource> dead, List<TestResource> alive, List<TestResource> undetermined, List<TestResource> removed, List<SweepError> errors)
    {
        var builder = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        var rows = await QueryDatabaseRowsAsync(connection);

        foreach (var (name, comment, connections) in rows)
        {
            var classified = ClassifyDatabaseRow(name, comment, connections, now, maxAge, workingCopyRoot, onlyRunKey);
            if (classified is null)
                continue; // not ours by name, or excluded by --run-key -- never touched, not even listed

            var (resource, eligibleForDeletion) = classified.Value;

            if (eligibleForDeletion)
            {
                dead.Add(resource);
                if (apply)
                {
                    try
                    {
                        // Sweeper is, by construction, a different process from whichever run created
                        // this database, so TestDatabaseNaming.EnsureOwnedByThisRun can never pass here --
                        // DropLeakedAsync is the sweeper-specific, cross-process-safe drop path.
                        await TestDatabaseLease.DropLeakedAsync(connection, name);
                        removed.Add(resource);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new SweepError(name, $"\u043d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0443\u0434\u0430\u043b\u0438\u0442\u044c \u0431\u0430\u0437\u0443 {name}: {ex.Message}"));
                    }
                }
            }
            else if (resource.Liveness == "alive")
            {
                alive.Add(resource);
            }
            else
            {
                undetermined.Add(resource);
            }
        }
    }

    /// <summary>Read-only listing of the sbtest_* databases on the given server -- used both by `sweep`
    /// (which may then act on the "dead" ones) and, read-only, by `status` (review blocker B1: `status`
    /// used to report zero database resources ever, so the section 88 acceptance check "after five runs,
    /// status --json contains no testResources[]" passed vacuously). Never issues DROP.</summary>
    internal static async Task<TestResource[]> ListDatabaseResourcesAsync(string serverConnectionString, string workingCopyRoot, DateTimeOffset now, TimeSpan maxAge)
    {
        var builder = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        var rows = await QueryDatabaseRowsAsync(connection);
        var resources = new List<TestResource>();
        foreach (var (name, comment, connections) in rows)
        {
            var classified = ClassifyDatabaseRow(name, comment, connections, now, maxAge, workingCopyRoot, onlyRunKey: null);
            if (classified is not null)
                resources.Add(classified.Value.Resource);
        }

        return resources.ToArray();
    }

    private static async Task<List<(string Name, string? Comment, long Connections)>> QueryDatabaseRowsAsync(NpgsqlConnection connection)
    {
        const string listSql = """
            select d.datname,
                   shobj_description(d.oid, 'pg_database') as comment,
                   (select count(*) from pg_stat_activity a where a.datname = d.datname) as connections
            from pg_database d
            where d.datname like 'sbtest\_%' escape '\'
            """;

        var rows = new List<(string Name, string? Comment, long Connections)>();
        await using var command = new NpgsqlCommand(listSql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2)));

        return rows;
    }

    /// <summary>
    /// Classifies one sbtest_* database row into a <see cref="TestResource"/> plus whether it is
    /// eligible for deletion right now, applying the exact same dead/alive/undetermined conjunction
    /// regardless of caller (sweep vs. status) -- see section 70.3 and review blocker B2. Returns null when
    /// the name isn't formally disposable at all, or when <paramref name="onlyRunKey"/> excludes it.
    /// A pure function over its inputs: no I/O, no clock reads beyond <paramref name="now"/> --
    /// unit-testable without Docker/Postgres/a real process.
    /// </summary>
    internal static (TestResource Resource, bool EligibleForDeletion)? ClassifyDatabaseRow(
        string name, string? comment, long connections, DateTimeOffset now, TimeSpan maxAge, string workingCopyRoot, string? onlyRunKey)
    {
        if (!TestDatabaseNaming.IsDisposable(name))
            return null; // not ours by name -- never touched, not even listed

        var metadata = ResourceLabels.TryParseComment(comment);
        var runKey = metadata?.RunKey ?? name.Split('_', 3).ElementAtOrDefault(1) ?? "unknown0";
        var slot = name.Split('_', 3).ElementAtOrDefault(2);
        if (onlyRunKey is not null && runKey != onlyRunKey)
            return null;

        var metadataReadable = metadata is not null;

        // Review blocker B2: metadata.Pid is only comparable to THIS machine's process table when
        // the database was created on THIS machine. A PID recorded by a different host (e.g. another
        // CI runner, or a teammate's laptop pointed at a shared "external" dev Postgres) is just a
        // number that happens not to exist locally -- IsProcessAlive(that number) would almost always
        // come back false, which used to be read as "the owning process is confirmed dead". Treat PID
        // evidence as unusable whenever the recorded host doesn't match this one, and never let an
        // unusable PID contribute to a "dead" verdict.
        var hostMatchesThisMachine = metadataReadable && string.Equals(metadata!.Host, Environment.MachineName, StringComparison.Ordinal);
        var pidEvidenceUnusable = metadataReadable && !hostMatchesThisMachine;
        var processAlive = hostMatchesThisMachine && IsProcessAlive(metadata!.Pid);

        var age = metadataReadable ? now - metadata!.StartedAtUtc : (TimeSpan?)null;
        var ageSeconds = age is null ? 0 : (int)Math.Max(0, age.Value.TotalSeconds);
        var mine = metadata?.Workdir == workingCopyRoot;

        // section 86.1/70.3, tightened per review blocker B2: --run-key lifts only the "metadata could not
        // be read at all" gate (comment missing or malformed, so nothing -- including age -- could be
        // computed about the database). It never lifts the age gate: a database with readable
        // metadata is only "dead" once it is actually older than --max-age, run-key or not. This is
        // what stops `sweep --apply --run-key <key seen in someone else's CI log>` from deleting a
        // database that is merely between test classes (0 connections) on someone else's machine.
        var forcedByRunKey = onlyRunKey is not null;
        var eligibleByAge = age is { } a && a > maxAge;
        var eligibleByForcedUnreadableMetadata = !metadataReadable && forcedByRunKey;

        var eligibleForDeletion = connections == 0 && !processAlive && !pidEvidenceUnusable && (eligibleByAge || eligibleByForcedUnreadableMetadata);
        var liveness = eligibleForDeletion ? "dead" : connections > 0 || processAlive ? "alive" : "undetermined";

        var resource = new TestResource(
            Kind: "database",
            Id: name,
            RunKey: runKey,
            Slot: slot,
            TestClass: null,
            Workdir: metadata?.Workdir,
            StartedAtUtc: metadataReadable ? TestKitJson.ToIso8601(metadata!.StartedAtUtc) : TestKitJson.ToIso8601(now),
            AgeSeconds: ageSeconds,
            HostPid: metadata?.Pid,
            HostPidAlive: pidEvidenceUnusable ? null : (metadataReadable ? processAlive : null),
            Connections: (int)connections,
            Mine: mine,
            Liveness: liveness);

        return (resource, eligibleForDeletion);
    }

    internal static bool IsProcessAlive(int pid)
    {
        try
        {
            _ = Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string FormatAge(int ageSeconds) =>
        $"{ageSeconds / 3600}h{(ageSeconds % 3600) / 60:D2}m";

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            await RunDockerAsync(["info", "--format", "{{.ServerVersion}}"]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static Task<string> RunDockerAsync(string arguments) => RunDockerAsync(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    internal static async Task<string> RunDockerAsync(IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить docker.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker {string.Join(' ', arguments)} завершился с кодом {process.ExitCode}: {stderr}");
        return stdout;
    }
}
