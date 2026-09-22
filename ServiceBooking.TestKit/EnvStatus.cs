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

        var (projectName, _) = ResolveComposeProjectName(workingCopyRoot, envFile);
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
        var envFile = DotEnvFile.Load(workingCopyRoot);

        var dockerAvailable = await IsDockerAvailableAsync();
        var externalConnectionSet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION"));

        var checks = new List<DoctorCheck>
        {
            // Review finding N13: this check used to be Ok = dockerAvailable, unconditionally, so a
            // developer with Docker Desktop stopped but a working SERVICEBOOKING_TEST_CONNECTION (a
            // perfectly valid, working server-mode configuration — the very case the "test-connection-env"
            // check below considers Ok) still got `doctor` exit code 2. Docker itself is only required
            // when there's no external server to fall back on.
            new(
                "docker-available",
                dockerAvailable || externalConnectionSet,
                dockerAvailable
                    ? "Docker доступен."
                    : externalConnectionSet
                        ? "Docker недоступен, но SERVICEBOOKING_TEST_CONNECTION задан — прогон пойдёт в server-режиме."
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

        // Review finding N13: IsDockerAvailableAsync/DotEnvFile.Load used to each run a second time inside
        // CheckPortsFreeAsync — an extra `docker info` round trip and a second file read for values this
        // method already has.
        checks.Add(await CheckPortsFreeAsync(workingCopyRoot, envFile, dockerAvailable));
        checks.Add(CheckRyukEnabled());

        var (budgetCheck, parallelism) = await CheckParallelConnectionBudgetAsync(workingCopyRoot, dockerAvailable);
        checks.Add(budgetCheck);

        var ok = checks.All(c => c.Ok);
        var exitCode = ok ? 0 : 2;

        if (asJson)
        {
            var document = new DoctorDocument(
                Command: "doctor",
                SchemaVersion: TestKitJson.SchemaVersion,
                GeneratedAtUtc: TestKitJson.ToIso8601(DateTimeOffset.UtcNow),
                ExitCode: exitCode,
                Checks: checks.ToArray(),
                Parallelism: parallelism);

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

    private static async Task<DoctorCheck> CheckPortsFreeAsync(string workingCopyRoot, IReadOnlyDictionary<string, string> envFile, bool dockerAvailable)
    {
        var (projectName, _) = ResolveComposeProjectName(workingCopyRoot, envFile);

        var ports = await Task.WhenAll(new[]
        {
            ResolvePort("SB_DB_PORT", envFile, 5432),
            ResolvePort("SB_API_PORT", envFile, 5000),
            ResolvePort("SB_WEB_PORT", envFile, 5173),
        }.Select(p => BuildPortInfoAsync(p, projectName, dockerAvailable)));

        return BuildPortsFreeCheck(ports);
    }

    /// <summary>Finding 2 (T8, doctor false-negative): SB_DB_PORT/SB_API_PORT/SB_WEB_PORT are the ports
    /// `docker compose up` (the dev stack) publishes — NOT ports the test run itself binds to. In
    /// container mode Testcontainers picks an ephemeral, dynamically-assigned host port for Postgres;
    /// in server mode the run talks to whatever SERVICEBOOKING_TEST_CONNECTION points at. Either way,
    /// a neighbour occupying 5432/5000 on the developer's own machine (e.g. a locally-installed
    /// Postgres.app, or an already-running API) does not stop a test run — it only affects `docker
    /// compose up`, a separate readiness question `status` already answers. This check is therefore
    /// informational only (`ok` is always true, never contributes to doctor's exit code 2, API_CONTRACT_CYCLE8.md
    /// §86.2/§86.3) — it still reports genuine neighbour conflicts, just as a warning rather than a
    /// blocking failure. Pure given already-resolved <see cref="PortInfo"/>s, so it is unit-testable
    /// without binding real sockets.</summary>
    internal static DoctorCheck BuildPortsFreeCheck(IReadOnlyList<PortInfo> ports)
    {
        var conflicts = ports.Where(p => p.InUse && !p.OwnedByThisCopy).ToArray();
        return new DoctorCheck(
            "ports-free",
            true,
            conflicts.Length == 0
                ? "Порты dev-стека свободны или заняты этой же рабочей копией."
                : $"Предупреждение (не блокирует doctor): заняты соседом — " +
                  $"{string.Join(", ", conflicts.Select(c => $"{c.Name}={c.Value}"))}. Это порты dev-стека " +
                  "(`docker compose up`), а не порты самого тестового прогона: container-режим поднимает " +
                  "Postgres на отдельном динамическом порту, server-режим использует SERVICEBOOKING_TEST_CONNECTION " +
                  "— ни один не занимает SB_DB_PORT/SB_API_PORT/SB_WEB_PORT напрямую. Что сделать, если это мешает " +
                  "именно `docker compose up`: задайте SB_*_PORT для этой рабочей копии, см. TestKit status.");
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

    /// <summary>ARCHITECTURE_CYCLE8_PHASE2.md §93.4/§102 (T8-P9): the same arithmetic
    /// TestRunEnvironment.EnsureConnectionBudgetAsync enforces fail-fast at the start of a run, but run
    /// here so `doctor --json` answers "does my parallelism fit the server?" BEFORE a run even starts.
    /// Every number in the formula is read from a single source, never duplicated as a literal:
    ///   - poolSizePerHost / hostsPerClass-implied server ceiling come from TestInfrastructure;
    ///   - maxParallelThreads comes from the same override env var / xunit.runner.json convention
    ///     ServiceBooking.Tests/TestParallelism.cs documents (TestKit cannot reference that project —
    ///     ServiceBooking.Tests references TestKit, not the other way around — so the file is read here
    ///     directly instead of re-declaring the number);
    ///   - serverMaxConnections comes from SHOW max_connections against the configured external server
    ///     when SERVICEBOOKING_TEST_CONNECTION is set, or from parsing TestInfrastructure.PostgresCommand
    ///     (the same array the ephemeral Testcontainers instance is actually booted with) when running in
    ///     container mode — never a second hardcoded "300".</summary>
    private static async Task<(DoctorCheck Check, ParallelismInfo Parallelism)> CheckParallelConnectionBudgetAsync(
        string workingCopyRoot, bool dockerAvailable)
    {
        const int hostsPerClass = 2; // §92.4: class fixture's own host + one dedicated per-test factory.
        const int fallbackMaxParallelThreads = 4; // xunit.runner.json's own default (§93.1), used only if the file can't be read.

        var maxParallelThreads = ResolveMaxParallelThreads(workingCopyRoot, fallbackMaxParallelThreads);
        var poolSizePerHost = TestInfrastructure.PoolMaxSize;
        var required = RequiredConnections(maxParallelThreads, hostsPerClass, poolSizePerHost);

        var externalConnection = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION");
        int? serverMaxConnections;
        string serverSource;

        if (!string.IsNullOrWhiteSpace(externalConnection))
        {
            serverMaxConnections = await TryReadExternalMaxConnectionsAsync(externalConnection);
            serverSource = serverMaxConnections is null
                ? "SERVICEBOOKING_TEST_CONNECTION задан, но сервер недоступен — проверка не выполнена."
                : $"SHOW max_connections на внешнем сервере (SERVICEBOOKING_TEST_CONNECTION) = {serverMaxConnections}.";
        }
        else if (dockerAvailable)
        {
            serverMaxConnections = ReadContainerMaxConnections();
            serverSource = $"container-режим: ephemeral Postgres поднимается с max_connections={serverMaxConnections} (TestInfrastructure.PostgresCommand).";
        }
        else
        {
            serverMaxConnections = null;
            serverSource = "ни SERVICEBOOKING_TEST_CONNECTION, ни Docker недоступны — проверка не выполнена.";
        }

        var parallelism = new ParallelismInfo(
            MaxParallelThreads: maxParallelThreads,
            PoolSizePerHost: poolSizePerHost,
            HostsPerClass: hostsPerClass,
            RequiredConnections: required,
            ServerMaxConnections: serverMaxConnections);

        if (serverMaxConnections is null)
        {
            return (new DoctorCheck(
                "parallel-connection-budget",
                true,
                $"Не удалось определить max_connections сервера: {serverSource} Нужно {required} соединений при " +
                $"P={maxParallelThreads}, пул={poolSizePerHost}, хостов на класс={hostsPerClass}."), parallelism);
        }

        var safeLimit = SafeConnectionLimit(serverMaxConnections.Value);
        var ok = FitsConnectionBudget(required, serverMaxConnections.Value);

        var detail = ok
            ? $"Бюджет сходится: нужно {required} соединений (P={maxParallelThreads}, пул={poolSizePerHost}, " +
              $"хостов на класс={hostsPerClass}) из безопасных {safeLimit:F0} (max_connections={serverMaxConnections}). {serverSource}"
            : $"Бюджет не сходится: нужно {required} соединений (P={maxParallelThreads}, пул={poolSizePerHost}, " +
              $"хостов на класс={hostsPerClass}), безопасный предел {safeLimit:F0} из max_connections={serverMaxConnections}. " +
              "Что сделать (любое из): " +
              "1) снизить параллелизм — ДВА места должны совпадать: SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS=2 " +
              "(эта переменная влияет только на арифметику ЭТОЙ проверки) И фактический параллелизм раннера, " +
              "который задаётся отдельно: `-- xUnit.MaxParallelThreads=2` в командной строке dotnet test либо " +
              "maxParallelThreads в ServiceBooking.Tests/xunit.runner.json — иначе раннер продолжит параллелить " +
              "на старом значении, и лимит соединений вылезет посреди прогона; " +
              $"2) поднять потолок сервера: max_connections >= {(int)Math.Ceiling(required / 0.9)}; " +
              "3) убрать SERVICEBOOKING_TEST_CONNECTION и дать прогону поднять свой контейнер (там потолок 300).";

        return (new DoctorCheck("parallel-connection-budget", ok, detail), parallelism);
    }

    /// <summary>T8-P9 review: no unit coverage existed for the budget arithmetic (verified only manually
    /// via CLI + ajv per backend's report). Pure, no I/O — testable directly from ServiceBooking.UnitTests
    /// via InternalsVisibleTo. §93.4's formula, unchanged, just named and given a seam.</summary>
    internal static int RequiredConnections(int maxParallelThreads, int hostsPerClass, int poolSizePerHost) =>
        maxParallelThreads * hostsPerClass * poolSizePerHost + 4;

    /// <summary>The 90% safety margin from §93.4 — kept in one place so "safe limit" always means the
    /// same number in the detail message and in the Ok decision below.</summary>
    internal static double SafeConnectionLimit(int serverMaxConnections) => serverMaxConnections * 0.9;

    internal static bool FitsConnectionBudget(int required, int serverMaxConnections) =>
        required <= SafeConnectionLimit(serverMaxConnections);

    /// <summary>Mirrors ServiceBooking.Tests/Infrastructure/TestParallelism.cs's precedence (env var
    /// override, else xunit.runner.json's own maxParallelThreads, else a documented fallback) without
    /// referencing that project — TestKit is referenced BY ServiceBooking.Tests, not the reverse.</summary>
    internal static int ResolveMaxParallelThreads(string workingCopyRoot, int fallback)
    {
        var envValue = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS");
        if (int.TryParse(envValue, out var configured) && configured > 0)
            return configured;

        try
        {
            var path = Path.Combine(workingCopyRoot, "ServiceBooking.Tests", "xunit.runner.json");
            if (!File.Exists(path))
                return fallback;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("maxParallelThreads", out var value) && value.TryGetInt32(out var parsed) && parsed > 0)
                return parsed;

            return fallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return fallback;
        }
    }

    /// <summary>Reads the "300" straight out of TestInfrastructure.PostgresCommand — the same array the
    /// ephemeral Testcontainers instance is actually started with (§93.3) — instead of a second literal
    /// that could drift from it.</summary>
    private static int? ReadContainerMaxConnections() =>
        ParseMaxConnectionsFromCommand(TestInfrastructure.PostgresCommand);

    /// <summary>Pure parsing, split out of <see cref="ReadContainerMaxConnections"/> so the "-c
    /// key=value" scanning logic is testable without depending on TestInfrastructure.PostgresCommand's
    /// actual current value (which review finding N9-adjacent reasoning says must never be duplicated
    /// as a hardcoded literal in a test either — so tests feed it synthetic arrays instead).</summary>
    internal static int? ParseMaxConnectionsFromCommand(IReadOnlyList<string> command)
    {
        for (var i = 0; i < command.Count - 1; i++)
        {
            if (command[i] != "-c")
                continue;

            var pair = command[i + 1];
            var separator = pair.IndexOf('=');
            if (separator <= 0 || !pair[..separator].Equals("max_connections", StringComparison.Ordinal))
                continue;

            if (int.TryParse(pair[(separator + 1)..], out var value))
                return value;
        }

        return null;
    }

    private static async Task<int?> TryReadExternalMaxConnectionsAsync(string connectionString)
    {
        try
        {
            await using var connection = new Npgsql.NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand("SHOW max_connections", connection);
            var raw = (string?)await command.ExecuteScalarAsync();
            return raw is not null && int.TryParse(raw, out var value) ? value : null;
        }
        catch
        {
            // doctor is best-effort read-only reporting; an unreachable server means the budget check
            // simply cannot be evaluated (ServerMaxConnections stays null), not that doctor crashes.
            return null;
        }
    }

    /// <summary>Finding 3 (T8): commit 8b288a9 removed the top-level `name:` from docker-compose.yml
    /// (a parameterized name with a SB_PROJECT_NAME-based default gave two working copies with no `.env`
    /// the SAME project name, so `docker compose down -v` in one could destroy the other's volume — see
    /// ARCHITECTURE_CYCLE8_PHASE2.md §90's writeup). Since that commit compose derives the project name
    /// from the checkout directory, overridable only via compose's own `COMPOSE_PROJECT_NAME` — SB_PROJECT_NAME
    /// is not read by compose at all any more. This used to still read the old SB_PROJECT_NAME variable
    /// with a hardcoded "servicebooking" fallback, so `status`/`doctor` in a second working copy reported
    /// the WRONG project name (defaulting to the first copy's) and could misclassify its own dev-stack
    /// ports as "owned by a neighbour". Brought in line: COMPOSE_PROJECT_NAME is the override, and the
    /// fallback is derived from the directory the same way compose derives it.</summary>
    private static (string Value, string Source) ResolveComposeProjectName(string workingCopyRoot, IReadOnlyDictionary<string, string> envFile) =>
        ResolveVariable("COMPOSE_PROJECT_NAME", envFile, DeriveComposeProjectName(workingCopyRoot));

    /// <summary>Approximates compose-go's own project-name normalization (lower-case the directory
    /// basename, keep only `[a-z0-9_-]`, and require the result to start with an alphanumeric — compose's
    /// pattern is the same `^[a-z0-9][a-z0-9_-]*$` API_CONTRACT_CYCLE8.md §85.1 already documents for the
    /// override variable). Pure/no I/O so it's unit-testable without invoking `docker compose config`.
    /// This is a display/heuristic value for `status`/`doctor`'s "owned by me" comparison, not the
    /// authority on the real compose project name — an explicit COMPOSE_PROJECT_NAME in `.env` always
    /// wins over this, exactly as it does for compose itself.</summary>
    internal static string DeriveComposeProjectName(string workingCopyRoot)
    {
        var trimmedPath = workingCopyRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(trimmedPath);
        if (string.IsNullOrEmpty(dirName))
            return "servicebooking";

        var sanitized = new string(dirName
            .ToLowerInvariant()
            .Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '_' or '-')
            .ToArray());

        var start = 0;
        while (start < sanitized.Length && !((sanitized[start] >= 'a' && sanitized[start] <= 'z') || (sanitized[start] >= '0' && sanitized[start] <= '9')))
            start++;

        var normalized = start < sanitized.Length ? sanitized[start..] : string.Empty;
        return normalized.Length == 0 ? "servicebooking" : normalized;
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
            // Review finding N8: the `docker ps` + `docker inspect` + label parsing here used to be a
            // ~40-line near-duplicate of Sweeper.SweepContainersAsync, and the two copies had already
            // drifted once (that's how status/sweep disagreed about liveness before — see
            // ClassifyContainerLiveness's own docs below). Shares Sweeper.ListLabeledContainersAsync
            // instead so there is exactly one place that knows how to read a labeled container.
            var containers = await Sweeper.ListLabeledContainersAsync(now);
            foreach (var container in containers)
            {
                var hostPidAlive = container.HostPid is null ? (bool?)null : Sweeper.IsProcessAlive(container.HostPid.Value);
                var age = (int)Math.Max(0, (now - container.StartedAt).TotalSeconds);
                var mine = container.Workdir == workingCopyRoot;

                var liveness = ClassifyContainerLiveness(hostPidAlive, age);

                resources.Add(new TestResource(
                    Kind: "container",
                    Id: container.Name,
                    RunKey: container.RunKey,
                    Slot: null,
                    TestClass: null,
                    Workdir: container.Workdir,
                    StartedAtUtc: TestKitJson.ToIso8601(container.StartedAt),
                    AgeSeconds: age,
                    HostPid: container.HostPid,
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
