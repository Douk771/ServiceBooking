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
        [WorkdirLabel] = TestInfrastructure.WorkingCopyRoot,
    };

    /// <summary>JSON payload written via <c>COMMENT ON DATABASE</c> for server-mode (external) databases —
    /// see ARCHITECTURE_CYCLE8.md §70.3, point 3. Read back by the sweeper via <see cref="TryParseComment"/>.
    ///
    /// <paramref name="ClassName"/> (T9 review, M3): ARCHITECTURE_CYCLE8_PHASE2.md §91.3/§92.2 and
    /// contracts/cycle8/testkit-status.schema.json's <c>testClass</c> field both promise that the
    /// slot↔class pairing is recorded somewhere a stuck database's owner can be identified from — but
    /// nothing ever wrote it. Optional (defaults to null) because it is written in a SECOND pass, via
    /// <see cref="TestDatabaseLease.RecordTestClassAsync"/>, after the database already exists: xUnit v2
    /// never hands a class fixture its own class' <see cref="Type"/> at <c>CREATE DATABASE</c> time
    /// (§92.2's own note), so the class name is only knowable once a test-base constructor first runs. A
    /// database that is dropped before any test in its class ever constructs (init failed before the
    /// first <c>[Fact]</c>) simply keeps ClassName=null — same as before this field existed.</summary>
    public sealed record DatabaseMetadata(string RunKey, string Host, int Pid, DateTimeOffset StartedAtUtc, string Workdir, string? ClassName = null);

    public static string ToComment(DatabaseMetadata metadata) => JsonSerializer.Serialize(metadata, TestKitJson.Options);

    /// <summary>Parses a <c>COMMENT ON DATABASE</c> payload written by <see cref="ToComment"/>. Returns
    /// null both on malformed JSON and on a JSON object that parses but is missing the fields required
    /// to compute an age — a database with unreadable metadata must be treated as "undetermined", never
    /// as "started at the Unix epoch" (which would make it look millennia old and eligible for deletion).</summary>
    public static DatabaseMetadata? TryParseComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        try
        {
            var metadata = JsonSerializer.Deserialize<DatabaseMetadata>(comment, TestKitJson.Options);
            if (metadata is null)
                return null;

            if (string.IsNullOrWhiteSpace(metadata.RunKey) || metadata.StartedAtUtc == default)
                return null;

            return metadata;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
