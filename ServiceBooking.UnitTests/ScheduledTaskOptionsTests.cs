using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Services.Scheduling;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §27.2 — the one additive change to the existing scheduling
/// component this cycle makes: PeriodSeconds, when present, wins over PeriodMinutes.</summary>
public class ScheduledTaskOptionsTests
{
    private sealed class FakeTask : IScheduledTask
    {
        public string Name => "fake-task";
        public TimeSpan DefaultPeriod => TimeSpan.FromMinutes(42);
        public Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void For_NoOverrides_UsesTaskDefaultPeriod()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var options = ScheduledTaskOptions.For(config, new FakeTask());
        options.Period.Should().Be(TimeSpan.FromMinutes(42));
    }

    [Fact]
    public void For_PeriodMinutesOnly_UsesPeriodMinutes()
    {
        var config = BuildConfig(new() { ["ScheduledTasks:fake-task:PeriodMinutes"] = "5" });
        var options = ScheduledTaskOptions.For(config, new FakeTask());
        options.Period.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void For_PeriodSecondsOnly_UsesPeriodSeconds()
    {
        var config = BuildConfig(new() { ["ScheduledTasks:fake-task:PeriodSeconds"] = "3" });
        var options = ScheduledTaskOptions.For(config, new FakeTask());
        options.Period.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void For_BothConfigured_PeriodSecondsWinsOverPeriodMinutes()
    {
        var config = BuildConfig(new()
        {
            ["ScheduledTasks:fake-task:PeriodMinutes"] = "5",
            ["ScheduledTasks:fake-task:PeriodSeconds"] = "2",
        });
        var options = ScheduledTaskOptions.For(config, new FakeTask());
        options.Period.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void For_EnabledDefaultsTrue_MaxRunMinutesDefaultsTen()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var options = ScheduledTaskOptions.For(config, new FakeTask());
        options.Enabled.Should().BeTrue();
        options.MaxRunTime.Should().Be(TimeSpan.FromMinutes(10));
    }
}
