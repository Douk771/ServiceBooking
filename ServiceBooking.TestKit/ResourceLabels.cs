using System.Text.Json;

namespace ServiceBooking.TestKit;

/// <summary>
/// Metadata attached to disposable test resources so the sweeper (§70.3) can tell "mine, dead" from
/// "mine, alive" from "someone else's" without guessing. Containers get Docker labels; server-mode
/// databases get the same information serialized into <c>COMMENT ON DATABASE</c> (Postgres has no
/// arbitrary key/value store for databases).
/// </summary>
public static class ResourceLabels
{
    /// <summary>Marks a container/database as belonging to ServiceBooking's test infrastructure.</summary>
    public const string OwnerLabel = "com.servicebooking.test";

    public const string RunKeyLabel = "com.servicebooking.test.run-key";
    public const string HostPidLabel = "com.servicebooking.test.host-pid";
    public const string StartedAtLabel = "com.servicebooking.test.started-at";
    public const string WorkdirLabel = "com.servicebooking.test.workdir";

    /// <summary>Docker labels applied to the ephemeral Postgres container.</summary>
    public static IDictionary<string, string> ForContainer(string runKey) => new Dictionary<string, string>
    {
        [OwnerLabel] = "1",
        [RunKeyLabel] = runKey,
        [HostPidLabel] = Environment.ProcessId.ToString(),
        [StartedAtLabel] = DateTimeOffset.UtcNow.ToString("O"),
        [WorkdirLabel] = Environment.CurrentDirectory,
    };

    /// <summary>JSON payload written via <c>COMMENT ON DATABASE</c> for server-mode (external) databases —
    /// see ARCHITECTURE_CYCLE8.md §70.3, point 3. Read back by the sweeper via <see cref="TryParseComment"/>.</summary>
    public sealed record DatabaseMetadata(string RunKey, string Host, int Pid, DateTimeOffset StartedAtUtc, string Workdir);

    public static string ToComment(DatabaseMetadata metadata) => JsonSerializer.Serialize(metadata);

    public static DatabaseMetadata? TryParseComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DatabaseMetadata>(comment);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
