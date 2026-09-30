using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services.Scheduling;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §499.5 — scheduler lanes: the lane of a task and the tick of a lane.</summary>
public class ScheduledTaskLaneTests
{
    private sealed class FakeTask(string name) : IScheduledTask
    {
        public string Name => name;
        public TimeSpan DefaultPeriod => TimeSpan.FromMinutes(1);
        public Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Lane_DefaultsToMain() =>
        ScheduledTaskOptions.For(Config([]), new FakeTask("x")).Lane.Should().Be("main");

    [Fact]
    public void Lane_IsReadFromConfiguration() =>
        ScheduledTaskOptions.For(Config(new() { ["ScheduledTasks:x:Lane"] = "realtime", ["ScheduledTasks:x:PeriodSeconds"] = "5" }), new FakeTask("x"))
            .Should().Match<ScheduledTaskOptions>(o => o.Lane == "realtime" && o.Period == TimeSpan.FromSeconds(5));

    [Fact]
    public void BlankLane_IsMain() =>
        ScheduledTaskOptions.For(Config(new() { ["ScheduledTasks:x:Lane"] = "  " }), new FakeTask("x")).Lane.Should().Be("main");

    [Fact]
    public void Tick_LaneSpecific_ElseCommon_ElseSixty()
    {
        var cfg = Config(new() { ["ScheduledTasks:TickSeconds"] = "10", ["ScheduledTasks:Lanes:realtime:TickSeconds"] = "5" });
        ScheduledTaskOptions.TickOf(cfg, "realtime").Should().Be(TimeSpan.FromSeconds(5));
        ScheduledTaskOptions.TickOf(cfg, "main").Should().Be(TimeSpan.FromSeconds(10));
        ScheduledTaskOptions.TickOf(Config([]), "main").Should().Be(TimeSpan.FromSeconds(60));
    }
}
