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
        var maxAgeArgIndex = Array.IndexOf(args, "--max-age");
        if (maxAgeArgIndex >= 0 && maxAgeArgIndex + 1 < args.Length)
            maxAge = ParseAge(args[maxAgeArgIndex + 1]);

        var onlyRunKeyIndex = Array.IndexOf(args, "--run-key");
        string? onlyRunKey = onlyRunKeyIndex >= 0 && onlyRunKeyIndex + 1 < args.Length ? args[onlyRunKeyIndex + 1] : null;

        var now = DateTimeOffset.UtcNow;
        var workingCopyRoot = Directory.GetCurrentDirectory();

        var dead = new List<TestResource>();
        var alive = new List<TestResource>();
        var undetermined = new List<TestResource>();
        var removed = new List<TestResource>();
        var errors = new List<string>();

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
                Removed: removed.ToArray());

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
                Console.Error.WriteLine("[sb-sweep] ОШИБКА: " + e);
        }

        return exitCode;
    }

    private static string Describe(TestResource r) =>
        r.Kind == "container"
            ? $"container {r.Id}  age={FormatAge(r.AgeSeconds)}  pid={r.HostPid}({(r.HostPidAlive == true ? "жив" : "нет")})"
            : $"database  {r.Id}  age={FormatAge(r.AgeSeconds)}  conns={r.Connections}";

    private static TimeSpan ParseAge(string value)
    {
        // "30m", "2h" — the only two units the architecture examples use (§70.3).
        if (value.EndsWith('m') && int.TryParse(value[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);
        if (value.EndsWith('h') && int.TryParse(value[..^1], out var hours))
            return TimeSpan.FromHours(hours);
        throw new TestSafetyException($"[sb-sweep] Не понимаю --max-age \"{value}\". Ожидался формат вроде 30m или 2h.");
    }

    private static async Task SweepContainersAsync(string workingCopyRoot, DateTimeOffset now, TimeSpan maxAge, string? onlyRunKey, bool apply,
        List<TestResource> dead, List<TestResource> alive, List<TestResource> undetermined, List<TestResource> removed, List<string> errors)
    {
        string psOutput;
        try
        {
            psOutput = await RunDockerAsync(["ps", "-a", "--filter", $"label={ResourceLabels.OwnerLabel}=1", "--format", "{{json .}}"]);
        }
        catch (Exception ex)
        {
            errors.Add($"docker недоступен, контейнеры пропущены: {ex.Message}");
            return;
        }

        foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var id = doc.RootElement.GetProperty("ID").GetString()!;
            var name = doc.RootElement.GetProperty("Names").GetString() ?? id;

            var inspect = await RunDockerAsync($"inspect {id}");
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

            if (!processAlive && ageSeconds > maxAge.TotalSeconds)
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
                        errors.Add($"не удалось удалить контейнер {name}: {ex.Message}");
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
        bool apply, List<TestResource> dead, List<TestResource> alive, List<TestResource> undetermined, List<TestResource> removed, List<string> errors)
    {
        var builder = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        const string listSql = """
            select d.datname,
                   shobj_description(d.oid, 'pg_database') as comment,
                   (select count(*) from pg_stat_activity a where a.datname = d.datname) as connections
            from pg_database d
            where d.datname like 'sbtest\_%' escape '\'
            """;

        var rows = new List<(string Name, string? Comment, long Connections)>();
        await using (var command = new NpgsqlCommand(listSql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2)));
        }

        foreach (var (name, comment, connections) in rows)
        {
            if (!TestDatabaseNaming.IsDisposable(name))
                continue; // not ours by name — never touched, not even listed

            var metadata = ResourceLabels.TryParseComment(comment);
            var runKey = metadata?.RunKey ?? name.Split('_', 3).ElementAtOrDefault(1) ?? "unknown0";
            var slot = name.Split('_', 3).ElementAtOrDefault(2);
            if (onlyRunKey is not null && runKey != onlyRunKey)
                continue;

            var age = metadata is not null ? now - metadata.StartedAtUtc : (TimeSpan?)null;
            var ageSeconds = age is null ? 0 : (int)Math.Max(0, age.Value.TotalSeconds);
            var processAlive = metadata is not null && IsProcessAlive(metadata.Pid);
            var mine = metadata?.Workdir == workingCopyRoot;

            var resource = new TestResource(
                Kind: "database",
                Id: name,
                RunKey: runKey,
                Slot: slot,
                TestClass: null,
                Workdir: metadata?.Workdir,
                StartedAtUtc: metadata is not null ? TestKitJson.ToIso8601(metadata.StartedAtUtc) : TestKitJson.ToIso8601(now),
                AgeSeconds: ageSeconds,
                HostPid: metadata?.Pid,
                HostPidAlive: metadata is null ? null : processAlive,
                Connections: (int)connections,
                Mine: mine,
                Liveness: "undetermined");

            if (connections == 0 && !processAlive && age is { } a && a > maxAge)
            {
                dead.Add(resource with { Liveness = "dead" });
                if (apply)
                {
                    try
                    {
                        await TestDatabaseLease.DropAsync(connection, name);
                        removed.Add(resource with { Liveness = "dead" });
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"не удалось удалить базу {name}: {ex.Message}");
                    }
                }
            }
            else if (connections > 0 || processAlive)
            {
                alive.Add(resource with { Liveness = "alive" });
            }
            else
            {
                undetermined.Add(resource with { Liveness = "undetermined" });
            }
        }
    }

    private static bool IsProcessAlive(int pid)
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
