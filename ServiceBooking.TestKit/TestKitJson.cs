using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBooking.TestKit;

/// <summary>
/// Машиночитаемая форма вывода CLI (status/sweep/doctor --json), см. §86 API_CONTRACT_CYCLE8.md и
/// contracts/cycle8/testkit-status.schema.json. Схема нормативна: эти типы — её отражение, а не
/// наоборот, см. правило §84.2.
/// </summary>
public static class TestKitJson
{
    public const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static string ToIso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    /// <summary>Пишет ТОЛЬКО JSON в stdout — §86.3. Человекочитаемое печатается вызывающим кодом в stderr.</summary>
    public static void WriteJson<T>(T document)
    {
        Console.Out.Write(JsonSerializer.Serialize(document, Options));
        Console.Out.Write('\n');
    }
}

public sealed record WorkingCopy(
    string Path,
    string ComposeProjectName,
    string DevDatabaseName,
    bool EnvFilePresent);

public sealed record PortInfo(
    string Name,
    int Value,
    string Source,
    bool InUse,
    bool OwnedByThisCopy);

public sealed record DockerInfo(
    bool Available,
    string? Error);

public sealed record TestResource(
    string Kind,
    string Id,
    string RunKey,
    string? Slot,
    string? TestClass,
    string? Workdir,
    string StartedAtUtc,
    int AgeSeconds,
    int? HostPid,
    bool? HostPidAlive,
    int? Connections,
    bool Mine,
    string Liveness);

public sealed record StatusDocument(
    string Command,
    int SchemaVersion,
    string GeneratedAtUtc,
    WorkingCopy WorkingCopy,
    PortInfo[] Ports,
    DockerInfo Docker,
    TestResource[] TestResources);

public sealed record DoctorCheck(
    string Name,
    bool Ok,
    string Detail);

public sealed record DoctorDocument(
    string Command,
    int SchemaVersion,
    string GeneratedAtUtc,
    int ExitCode,
    DoctorCheck[] Checks);

public sealed record SweepError(
    string? ResourceId,
    string Message);

public sealed record SweepDocument(
    string Command,
    int SchemaVersion,
    string GeneratedAtUtc,
    int ExitCode,
    bool Applied,
    int MaxAgeSeconds,
    string? RunKeyFilter,
    DockerInfo Docker,
    TestResource[] Dead,
    TestResource[] Alive,
    TestResource[] Undetermined,
    TestResource[] Removed,
    SweepError[] Errors);

/// <summary>
/// Простейший разбор `.env` (KEY=VALUE, `#`-комментарии, пустые строки) — нужен, чтобы CLI мог отличить
/// источник значения (`process-env` / `env-file` / `default`) буквально по приоритету §85.3.
/// Не предназначен для сложного синтаксиса dotenv (многострочные значения, экспорт) — рабочей копии
/// этого достаточно, т.к. `.env` здесь содержит только SB_*-переменные (§85.1).
/// </summary>
public static class DotEnvFile
{
    public static IReadOnlyDictionary<string, string> Load(string workingCopyRoot)
    {
        var path = Path.Combine(workingCopyRoot, ".env");
        var result = new Dictionary<string, string>();
        if (!File.Exists(path))
            return result;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            result[key] = value;
        }

        return result;
    }
}
