using FluentAssertions;
using ServiceBooking.LegalKit;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>`CliArgs.Parse` — ARCHITECTURE_CYCLE11.md §117.2's six flags, pure parsing, no I/O.</summary>
public class CliArgsTests
{
    [Fact]
    public void Parse_UnknownFlag_Throws()
    {
        var act = () => CliArgs.Parse(["--nonsense", "value"]);
        act.Should().Throw<CliUsageException>().Which.Message.Should().Contain("--nonsense");
    }

    [Fact]
    public void Parse_ValueFlagFollowedByAnotherFlag_DoesNotSwallowIt()
    {
        // The scenario named in review: `--values --json` must not silently read "--json" as the value
        // of --values.
        var act = () => CliArgs.Parse(["--values", "--json"]);
        act.Should().Throw<CliUsageException>().Which.Message.Should().Contain("--values");
    }

    [Fact]
    public void Parse_KnownSwitchesAndValues_ParseCorrectly()
    {
        var args = CliArgs.Parse(["--source", "a", "--out", "b", "--dry-run", "--json"]);

        args.Get("source").Should().Be("a");
        args.Get("out").Should().Be("b");
        args.Has("dry-run").Should().BeTrue();
        args.Has("json").Should().BeTrue();
    }

    [Fact]
    public void Parse_ValueFlagMissingItsValue_Throws()
    {
        var act = () => CliArgs.Parse(["--source"]);
        act.Should().Throw<CliUsageException>().Which.Message.Should().Contain("--source");
    }
}
