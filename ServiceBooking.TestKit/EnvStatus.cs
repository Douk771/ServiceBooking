using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ServiceBooking.TestKit;

/// <summary>
/// US-90: "what's in use" — read-only reporting, never a destructive action. No branch in this class
/// calls DROP, rm, or stop (§73.4, приёмочный критерий).
/// Машиночитаемый вывод (--json) обязан соответствовать contracts/cycle8/testkit-status.schema.json
/// (§86.3/§86.4): корневое поле command/schemaVersion/generatedAtUtc, workingCopy{...}, docker как
/// объект {available, error}, ports[] в camelCase, единый массив testResources[].
/// </summary>
public static class EnvStatus
{
    public static async Task<int> RunAsync(string[] args)
    {
        var asJson = args.Contains("--json");
        var workingCopyRoot = TestInfrastructure.WorkingCopyRoot;
        var envFile = DotEnvFile.Load(workingCopyRoot);
        var envFilePresent = File.Exists(Path.Combine(workingCopyRoot, ".env"));

        var (projectName, _) = ResolveVariable("SB_PROJECT_NAME", envFile, "servicebooking");
        var (dbName, _) = ResolveVariable("SB_DB_NAME", envFile, "servicebooking");

        var dbPort = ResolvePort("SB_DB_PORT", envFile, 5432);
        var apiPort = ResolvePort("SB_API_PORT", envFile, 5000);
        var webPort = ResolvePort("SB_WEB_PORT", envFile, 5173);
        var glitchtipPort = ResolvePort("SB_GLITCHTIP_PORT", envFile, 8000);

        var dockerAvailable = await IsDockerAvailableAsync();
        var dockerInfo = new DockerInfo(dockerAvailable, dockerAvailable ? null : "docker info завершился с ошибкой или docker недоступен");

        var ports = await Task.WhenAll(
            new[] { dbPort, apiPort, webPort, glitchtipPort }
                .Select(p => BuildPortInfoAsync(p, projectName, dockerAvailable)));

        var containerResources = dockerAvailable
            ? await CollectContainerResourcesAsync(workingCopyRoot)
            : [];
        var databaseResources = await CollectDatabaseResourcesAsync(workingCopyRoot);
        var testResources = containerResources.Concat(databaseResources).ToArray();

        if (asJson)
        {
            var document = new StatusDocument(
                Command: "status",
                SchemaVersion: TestKitJson.SchemaVersion,
                GeneratedAtUtc: TestKitJson.ToIso8601(DateTimeOffset.UtcNow),
                WorkingCopy: new WorkingCopy(workingCopyRoot, projectName, dbName, envFilePresent),
                Ports: ports,
                Docker: dockerInfo,
                TestResources: testResources);

            TestKitJson.WriteJson(document);
        }
        else
        {
            Console.Error.WriteLine($"[sb-status] рабочая копия: {workingCopyRoot}");
            Console.Error.WriteLine($"[sb-status] compose-проект: {projectName}, dev-база: {dbName}");
            Console.Error.WriteLine($"[sb-status] docker: {(dockerAvailable ? "доступен" : "недоступен")}");
            foreach (var p in ports)
                Console.Error.WriteLine($"[sb-status] {p.Name}={p.Value}  {(p.InUse ? "занят" : "свободен")} ({p.Source})");
            if (testResources.Length == 0)
                Console.Error.WriteLine("[sb-status] тестовых ресурсов не найдено");
            foreach (var r in testResources)
                Console.Error.WriteLine($"[sb-status] {r.Kind} {r.Id}  run={r.RunKey}  {(r.Mine ? "мой" : "чужой")}  {r.Liveness}");
        }

        // status никогда не возвращает ненулевой код из-за состояния машины — §86.2.
        return 0;
    }

    public static async Task<int> RunDoctorAsync(string[] args)
    {
        var asJson = args.Contains("--json");
        var workingCopyRoot = TestInfrastructure.WorkingCopyRoot;

        var dockerAvailable = await IsDockerAvailableAsync();
        var externalConnectionSet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION"));

        var checks = new List<DoctorCheck>
        {
            new(
                "docker-available",
                dockerAvailable,
                dockerAvailable
                    ? "Docker доступен."
                    : "Docker недоступен. Запустите Docker Desktop, либо задайте SERVICEBOOKING_TEST_CONNECTION " +
                      "для запуска против внешнего сервера."),
        };

        checks.Add(await CheckPostgresImageCachedAsync(dockerAvailable));
        checks.Add(CheckDotnetSdk());

        checks.Add(new(
            "test-connection-env",
            dockerAvailable || externalConnectionSet,
            dockerAvailable
                ? "Прогон поднимет Postgres сам (container-режим)."
                : externalConnectionSet
                    ? "SERVICEBOOKING_TEST_CONNECTION задан — прогон пойдёт в server-режиме."
                    : "Docker недоступен и SERVICEBOOKING_TEST_CONNECTION не задан. " +
                      "Что сделать: запустите Docker Desktop, либо задайте SERVICEBOOKING_TEST_CONNECTION."));

        checks.Add(await CheckPortsFreeAsync());
        checks.Add(CheckRyukEnabled());

        var ok = checks.All(c => c.Ok);
        var exitCode = ok ? 0 : 2;

        if (asJson)
        {
            var document = new DoctorDocument(
                Command: "doctor",
                SchemaVersion: TestKitJson.SchemaVersion,
                GeneratedAtUtc: TestKitJson.ToIso8601(DateTimeOffset.UtcNow),
                ExitCode: exitCode,
                Checks: checks.ToArray());

            TestKitJson.WriteJson(document);
        }
        else
        {
            foreach (var c in checks)
                Console.Error.WriteLine($"[sb-doctor] {(c.Ok ? "OK  " : "FAIL")} {c.Name}: {c.Detail}");
        }

        return exitCode;
    }

    private static async Task<DoctorCheck> CheckPostgresImageCachedAsync(bool dockerAvailable)
    {
        if (!dockerAvailable)
            return new DoctorCheck("postgres-image-cached", false, "Docker недоступен, проверить кеш образа нельзя.");

        try
        {
            var output = await Sweeper.RunDockerAsync(["images", "-q", TestInfrastructure.PostgresImage]);
            var cached = !string.IsNullOrWhiteSpace(output);
            return new DoctorCheck(
                "postgres-image-cached",
                cached,
                cached
                    ? $"Образ {TestInfrastructure.PostgresImage} в кеше."
                    : $"Образ {TestInfrastructure.PostgresImage} не в кеше. Что сделать: docker pull {TestInfrastructure.PostgresImage}.");
        }
        catch (Exception ex)
        {
            return new DoctorCheck("postgres-image-cached", false, $"Не удалось проверить кеш образа: {ex.Message}");
        }
    }

    private static DoctorCheck CheckDotnetSdk()
    {
        var version = Environment.Version;
        return new DoctorCheck("dotnet-sdk", true, $".NET SDK доступен (runtime {version}).");
    }

    private static async Task<DoctorCheck> CheckPortsFreeAsync()
    {
        var workingCopyRoot = TestInfrastructure.WorkingCopyRoot;
        var envFile = DotEnvFile.Load(workingCopyRoot);
        var (projectName, _) = ResolveVariable("SB_PROJECT_NAME", envFile, "servicebooking");
        var dockerAvailable = await IsDockerAvailableAsync();

        var ports = await Task.WhenAll(new[]
        {
            ResolvePort("SB_DB_PORT", envFile, 5432),
            ResolvePort("SB_API_PORT", envFile, 5000),
            ResolvePort("SB_WEB_PORT", envFile, 5173),
        }.Select(p => BuildPortInfoAsync(p, projectName, dockerAvailable)));

        var conflicts = ports.Where(p => p.InUse && !p.OwnedByThisCopy).ToArray();
        var ok = conflicts.Length == 0;
        return new DoctorCheck(
            "ports-free",
            ok,
            ok
                ? "Порты dev-стека свободны или заняты этой же рабочей копией."
                : $"Заняты соседом: {string.Join(", ", conflicts.Select(c => $"{c.Name}={c.Value}"))}. " +
                  "Что сделать: задайте SB_*_PORT для этой рабочей копии, см. TestKit status.");
    }

    private static DoctorCheck CheckRyukEnabled()
    {
        var disabled = string.Equals(
            Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        return new DoctorCheck(
            "ryuk-enabled",
            !disabled,
            disabled
                ? "TESTCONTAINERS_RYUK_DISABLED=true запрещено (§70.2). Что сделать: снимите переменную окружения."
                : "Ryuk не отключён.");
    }

    private static (string Value, string Source) ResolveVariable(string name, IReadOnlyDictionary<string, string> envFile, string fallback)
    {
        var processValue = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(processValue))
            return (processValue, "process-env");

        if (envFile.TryGetValue(name, out var fileValue) && !string.IsNullOrEmpty(fileValue))
            return (fileValue, "env-file");

        return (fallback, "default");
    }

    private static (string Name, int Value, string Source) ResolvePort(string name, IReadOnlyDictionary<string, string> envFile, int fallback)
    {
        var (raw, source) = ResolveVariable(name, envFile, fallback.ToString());
        return (name, int.TryParse(raw, out var v) ? v : fallback, source);
    }

    private static async Task<PortInfo> BuildPortInfoAsync((string Name, int Value, string Source) port, string projectName, bool dockerAvailable)
    {
        var inUse = IsPortBound(port.Value);
        var ownedByThisCopy = inUse && dockerAvailable && await IsPortOwnedByComposeProjectAsync(port.Value, projectName);
        return new PortInfo(port.Name, port.Value, port.Source, inUse, ownedByThisCopy);
    }

    /// <summary>Real ownership check (review blocker #6: this used to be a hard-coded `false`, which made
    /// `status`/`doctor` report every port a developer's own dev-stack was using as "занят чужим", and
    /// made `doctor` exit 2 for anyone with their own docker-compose stack running). Asks docker which
    /// compose project, if any, published this host port and compares it to SB_PROJECT_NAME.</summary>
    private static async Task<bool> IsPortOwnedByComposeProjectAsync(int port, string projectName)
    {
        try
        {
            var output = await Sweeper.RunDockerAsync(
                ["ps", "--filter", $"publish={port}", "--format", "{{.Label \"com.docker.compose.project\"}}"]);

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(composeProject => string.Equals(composeProject, projectName, StringComparison.Ordinal));
        }
        catch
        {
            // Best-effort: an unreadable answer must not be treated as ownership.
            return false;
        }
    }

    private static async Task<TestResource[]> CollectContainerResourcesAsync(string workingCopyRoot)
    {
        var resources = new List<TestResource>();
        var now = DateTimeOffset.UtcNow;

        try
        {
            var psOutput = await Sweeper.RunDockerAsync(["ps", "-a", "--filter", $"label={ResourceLabels.OwnerLabel}=1", "--format", "{{json .}}"]);
            foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var doc = JsonDocument.Parse(line);
                var id = doc.RootElement.GetProperty("ID").GetString()!;
                var name = doc.RootElement.GetProperty("Names").GetString() ?? id;

                var inspect = await Sweeper.RunDockerAsync($"inspect {id}");
                using var inspectDoc = JsonDocument.Parse(inspect);
                var labels = inspectDoc.RootElement[0].GetProperty("Config").GetProperty("Labels");

                var workdir = labels.TryGetProperty(ResourceLabels.WorkdirLabel, out var w) ? w.GetString() : null;
                var runKey = labels.TryGetProperty(ResourceLabels.RunKeyLabel, out var rk) ? rk.GetString() : null;
                var hostPidRaw = labels.TryGetProperty(ResourceLabels.HostPidLabel, out var pidProp) ? pidProp.GetString() : null;
                var startedAtRaw = labels.TryGetProperty(ResourceLabels.StartedAtLabel, out var saProp) ? saProp.GetString() : null;

                if (runKey is null)
                    continue;

                var hostPid = int.TryParse(hostPidRaw, out var pid) ? (int?)pid : null;
                var hostPidAlive = hostPid is null ? (bool?)null : IsProcessAlive(hostPid.Value);
                var startedAt = startedAtRaw is not null && DateTimeOffset.TryParse(startedAtRaw, out var sa) ? sa : now;
                var age = (int)Math.Max(0, (now - startedAt).TotalSeconds);
                var mine = workdir == workingCopyRoot;

                var liveness = ClassifyContainerLiveness(hostPidAlive, age);

                resources.Add(new TestResource(
                    Kind: "container",
                    Id: name,
                    RunKey: runKey,
                    Slot: null,
                    TestClass: null,
                    Workdir: workdir,
                    StartedAtUtc: TestKitJson.ToIso8601(startedAt),
                    AgeSeconds: age,
                    HostPid: hostPid,
                    HostPidAlive: hostPidAlive,
                    Connections: null,
                    Mine: mine,
                    Liveness: liveness));
            }
        }
        catch
        {
            // status is best-effort read-only reporting; a docker hiccup here just means an empty list.
        }

        return resources.ToArray();
    }

    /// <summary>Read-only reporting of live sbtest_* databases on the "external" server, when one is
    /// configured (review blocker B1: `status` used to only ever look at containers — Npgsql was never
    /// touched — so API_CONTRACT_CYCLE8.md §86.1's "живые тестовые контейнеры И базы" and the §88
    /// acceptance check were vacuously satisfied no matter what was actually left running). Reuses
    /// Sweeper's own classification (<see cref="Sweeper.ClassifyDatabaseRow"/>) so `status --json` and
    /// `sweep --json` can never disagree about the same database, exactly as already guaranteed for
    /// containers by <see cref="ClassifyContainerLiveness"/> above. Never calls DropLeakedAsync/DROP —
    /// only <see cref="Sweeper.ListDatabaseResourcesAsync"/>, which is read-only by construction.</summary>
    private static async Task<TestResource[]> CollectDatabaseResourcesAsync(string workingCopyRoot)
    {
        var externalConnection = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(externalConnection))
            return []; // container-mode databases live inside an ephemeral Testcontainers instance that
                        // no longer exists once the test process exits — there is nothing outside that
                        // process for a separately-run CLI to observe, same as Sweeper.RunAsync (Sweeper.cs:55-57).

        try
        {
            return await Sweeper.ListDatabaseResourcesAsync(
                externalConnection, workingCopyRoot, DateTimeOffset.UtcNow, TestInfrastructure.DefaultSweepMaxAge);
        }
        catch
        {
            // status is best-effort read-only reporting; an unreachable server here just means an empty list.
            return [];
        }
    }

    /// <summary>Same conjunction as Sweeper's container classification (§70.3), using the sweeper's
    /// default max-age threshold since status doesn't take --max-age. Previously this diverged from
    /// Sweeper (a container with hostPidAlive == false and low age was reported "alive" here but
    /// "undetermined" by sweep, and hostPidAlive == null behaved the same wrong way) — kept identical to
    /// Sweeper's `!processAlive && age > maxAge` / `processAlive` / else-undetermined so `status --json`
    /// and `sweep --json` never disagree about the same resource.</summary>
    internal static string ClassifyContainerLiveness(bool? hostPidAlive, int ageSeconds)
    {
        var maxAgeSeconds = TestInfrastructure.DefaultSweepMaxAge.TotalSeconds;
        var processAlive = hostPidAlive == true;

        if (!processAlive && ageSeconds > maxAgeSeconds)
            return "dead";
        if (processAlive)
            return "alive";
        return "undetermined";
    }

    private static bool IsPortBound(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            _ = System.Diagnostics.Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            await Sweeper.RunDockerAsync(["info", "--format", "{{.ServerVersion}}"]);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
