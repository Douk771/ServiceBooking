using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Покрывает <see cref="ResourceLabels.ToComment"/>/<see cref="ResourceLabels.TryParseComment"/> — чистая
/// сериализация/десериализация, без БД и без сети.
/// </summary>
public class ResourceLabelsTests
{
    [Fact]
    public void ToComment_uses_camelCase_per_ARCHITECTURE_CYCLE8_section_70_3()
    {
        var metadata = new ResourceLabels.DatabaseMetadata(
            RunKey: "a3f19c7b",
            Host: "myhost",
            Pid: 4242,
            StartedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Workdir: "/repo");

        var json = ResourceLabels.ToComment(metadata);

        json.Should().Contain("\"runKey\"");
        json.Should().Contain("\"host\"");
        json.Should().Contain("\"pid\"");
        json.Should().Contain("\"startedAtUtc\"");
        json.Should().Contain("\"workdir\"");
        json.Should().NotContain("\"RunKey\"");
    }

    [Fact]
    public void TryParseComment_round_trips_ToComment_output()
    {
        var original = new ResourceLabels.DatabaseMetadata("a3f19c7b", "myhost", 4242,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "/repo");

        var parsed = ResourceLabels.TryParseComment(ResourceLabels.ToComment(original));

        parsed.Should().Be(original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"RunKey":"a3f19c7b","Host":"h","Pid":1,"StartedAtUtc":"2026-01-01T00:00:00Z","Workdir":"/x"}""")]
    public void TryParseComment_returns_null_instead_of_a_zeroed_out_metadata_object(string? comment)
    {
        // Review blocker: PascalCase input (or anything that fails to populate RunKey/StartedAtUtc) used
        // to silently deserialize into a DatabaseMetadata with Pid=0 and StartedAtUtc=default, which made
        // the sweeper compute an age of roughly two thousand years and delete the database on --apply.
        // A database with unreadable metadata must come back as "undetermined", i.e. null here.
        ResourceLabels.TryParseComment(comment).Should().BeNull();
    }
}
