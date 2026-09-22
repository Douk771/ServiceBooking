using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace ServiceBooking.TestKit;

/// <summary>
/// Third line of defence for leaked test resources (§70.3): dry-run by default, requires --apply to
/// actually delete anything, and never touches a resource it can't classify as dead with confidence.
/// </summary>
public static class Sweeper
{
    public static async Task<int> RunAsync(string[] args)
    {
        var apply = args.Contains("--apply");
        var maxAge = TestInfrastructure.DefaultSweepMaxAge;
        var maxAgeArgIndex = Array.IndexOf(args, "--max-age");
        if (maxAgeArgIndex >= 0 && maxAgeArgIndex + 1 < args.Length)
            maxAge = ParseAge(args[maxAgeArgIndex + 1]);

        var onlyRunKeyIndex = Array.IndexOf(args, "--run-key");
        string? onlyRunKey = onlyRunKeyIndex >= 0 && onlyRunKeyIndex + 1 < args.Length ? args[onlyRunKeyIndex + 1] : null;

        var now = DateTimeOffset.UtcNow;

        var dead = new List<string>();
        var alive = new List<string>();
        var unknown = new List<string>();

        await SweepContainersAsync(now, maxAge, onlyRunKey, apply, dead, alive, unknown);

        var externalConnection = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(externalConnection))
            await SweepDatabasesAsync(externalConnection, now, maxAge, onlyRunKey, apply, dead, alive, unknown);

        if (dead.Count > 0)
        {
            Console.WriteLine(apply ? "[sb-sweep] Удалены:" : "[sb-sweep] Мёртвые (будут удалены с --apply):");
            foreach (var line in dead) Console.WriteLine("  " + line);
        }

        if (alive.Count > 0)
        {
            Console.WriteLine("[sb-sweep] Живые (не трогаю):");
            foreach (var line in alive) Console.WriteLine("  " + line);
        }

        if (unknown.Count > 0)
        {
            Console.WriteLine("[sb-sweep] Неопределённые (не трогаю, проверьте руками):");
            foreach (var line in unknown) Console.WriteLine("  " + line);
        }

        if (dead.Count == 0 && alive.Count == 0 && unknown.Count == 0)
            Console.WriteLine("[sb-sweep] Ничего не найдено.");

        return 0;
    }

    private static TimeSpan ParseAge(string value)
    {
        // "30m", "2h" — the only two units the architecture examples use (§70.3).
        if (value.EndsWith('m') && int.TryParse(value[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);
        if (value.EndsWith('h') && int.TryParse(value[..^1], out var hours))
            return TimeSpan.FromHours(hours);
        throw new TestSafetyException($"[sb-sweep] Не понимаю --max-age \"{value}\". Ожидался формат вроде 30m или 2h.");
    }

    private static async Task SweepContainersAsync(DateTimeOffset now, TimeSpan maxAge, string? onlyRunKey, bool apply,
        List<string> dead, List<string> alive, List<string> unknown)
    {
        string psOutput;
        try
        {
            psOutput = await RunDockerAsync(["ps", "-a", "--filter", $"label={ResourceLabels.OwnerLabel}=1", "--format", "{{json .}}"]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[sb-sweep] docker недоступен, контейнеры пропущены: {ex.Message}");
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
            if (onlyRunKey is not null && runKey != onlyRunKey)
                continue;

            var hostPidRaw = labels.TryGetProperty(ResourceLabels.HostPidLabel, out var pidProp) ? pidProp.GetString() : null;
            var startedAtRaw = labels.TryGetProperty(ResourceLabels.StartedAtLabel, out var saProp) ? saProp.GetString() : null;

            var processAlive = IsProcessAlive(hostPidRaw);
            var age = startedAtRaw is not null && DateTimeOffset.TryParse(startedAtRaw, out var started)
                ? now - started
                : (TimeSpan?)null;

            var label = $"container {name}  age={FormatAge(age)}  pid={hostPidRaw}({(processAlive ? "жив" : "нет")})";

            if (!processAlive && age is { } a && a > maxAge)
            {
                dead.Add(label);
                if (apply)
                    await RunDockerAsync($"rm -f {id}");
            }
            else if (processAlive)
            {
                alive.Add(label);
            }
            else
            {
                unknown.Add(label);
            }
        }
    }

    private static async Task SweepDatabasesAsync(string serverConnectionString, DateTimeOffset now, TimeSpan maxAge, string? onlyRunKey,
        bool apply, List<string> dead, List<string> alive, List<string> unknown)
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
            if (onlyRunKey is not null && metadata?.RunKey != onlyRunKey)
                continue;

            var age = metadata is not null ? now - metadata.StartedAtUtc : (TimeSpan?)null;
            var processAlive = metadata is not null && IsProcessAlive(metadata.Pid.ToString());

            var label = $"database  {name}  age={FormatAge(age)}  conns={connections}";

            if (connections == 0 && !processAlive && age is { } a && a > maxAge)
            {
                dead.Add(label);
                if (apply)
                {
                    await TestDatabaseLease.DropAsync(connection, name);
                }
            }
            else if (connections > 0 || processAlive)
            {
                alive.Add(label + (processAlive ? "" : ""));
            }
            else
            {
                unknown.Add(label + "  метка неполная или возраст неизвестен");
            }
        }
    }

    private static bool IsProcessAlive(string? pidRaw)
    {
        if (!int.TryParse(pidRaw, out var pid))
            return false;

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

    private static string FormatAge(TimeSpan? age) =>
        age is null ? "?" : $"{(int)age.Value.TotalHours}h{age.Value.Minutes:D2}m";

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
