using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §26.1 — bounds only; the actual value is randomized on purpose (see
/// the class's own doc comment for why, unlike NotificationTiming.JitterMinutes).</summary>
public class PauseGeneratorTests
{
    [Fact]
    public void Next_AlwaysWithinBounds_AcrossManySamples()
    {
        var generator = new PauseGenerator();
        for (var i = 0; i < 1000; i++)
        {
            var pause = generator.Next(5000, 15000);
            pause.TotalMilliseconds.Should().BeInRange(5000, 15000);
        }
    }

    [Fact]
    public void Next_EqualMinAndMax_ReturnsThatValue()
    {
        new PauseGenerator().Next(1000, 1000).Should().Be(TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public void Next_MaxLessThanMin_Throws()
    {
        var generator = new PauseGenerator();
        var act = () => generator.Next(15000, 5000);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Next_ManySamples_AreNotAllEqual()
    {
        var generator = new PauseGenerator();
        var samples = Enumerable.Range(0, 50).Select(_ => generator.Next(5000, 15000)).Distinct().ToList();
        samples.Count.Should().BeGreaterThan(1);
    }
}
