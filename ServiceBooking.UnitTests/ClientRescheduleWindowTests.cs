using FluentAssertions;
using ServiceBooking.API.Services.Bookings;

namespace ServiceBooking.UnitTests;

public class ClientRescheduleWindowTests
{
    [Fact]
    public void TryNormalize_Null_FailsAndDoesNotTouch() =>
        ClientRescheduleWindow.TryNormalize(null, out _).Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(168)]
    public void TryNormalize_WithinBounds_Succeeds(int raw)
    {
        ClientRescheduleWindow.TryNormalize(raw, out var hours).Should().BeTrue();
        hours.Should().Be(raw);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(169)]
    public void TryNormalize_OutOfBounds_Fails(int raw) =>
        ClientRescheduleWindow.TryNormalize(raw, out _).Should().BeFalse();

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(169, 2)]
    [InlineData(2, 2)]
    public void Normalize_GarbageInDb_FallsBackToDefault(int stored, int expected) =>
        ClientRescheduleWindow.Normalize(stored).Should().Be(expected);

    [Fact]
    public void IsWithinWindow_BothEndsFarEnough_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentVisit = now.AddHours(5);
        var newVisit = now.AddHours(10);
        ClientRescheduleWindow.IsWithinWindow(now, currentVisit, newVisit, minHours: 2).Should().BeTrue();
    }

    [Fact]
    public void IsWithinWindow_CurrentVisitTooClose_ReturnsFalse()
    {
        // Without checking the OLD end too, a client three days out could reschedule to "in 20 minutes".
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentVisit = now.AddHours(1); // < 2h min window
        var newVisit = now.AddDays(3);
        ClientRescheduleWindow.IsWithinWindow(now, currentVisit, newVisit, minHours: 2).Should().BeFalse();
    }

    [Fact]
    public void IsWithinWindow_NewVisitTooClose_ReturnsFalse()
    {
        // The second end of the rule: moving a far-out visit to "right now" must also be blocked.
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentVisit = now.AddDays(3);
        var newVisit = now.AddMinutes(20);
        ClientRescheduleWindow.IsWithinWindow(now, currentVisit, newVisit, minHours: 2).Should().BeFalse();
    }

    [Fact]
    public void IsWithinWindow_ExactlyOnTheBoundary_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentVisit = now.AddHours(2);
        var newVisit = now.AddHours(2);
        ClientRescheduleWindow.IsWithinWindow(now, currentVisit, newVisit, minHours: 2).Should().BeTrue();
    }

    [Fact]
    public void IsWithinWindow_ZeroMinHours_AllowsRightUpToVisitStart()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ClientRescheduleWindow.IsWithinWindow(now, now, now.AddMinutes(1), minHours: 0).Should().BeTrue();
    }

    // ARCHITECTURE_CYCLE17.md §304.1 — CanClientCancel: one end of the window (no "new visit" to
    // check, unlike reschedule).

    [Fact]
    public void CanClientCancel_VisitFarEnoughAway_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var visitStart = now.AddHours(5);
        ClientRescheduleWindow.CanClientCancel(now, visitStart, minHours: 2).Should().BeTrue();
    }

    [Fact]
    public void CanClientCancel_VisitTooClose_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var visitStart = now.AddHours(1);
        ClientRescheduleWindow.CanClientCancel(now, visitStart, minHours: 2).Should().BeFalse();
    }

    [Fact]
    public void CanClientCancel_ExactlyOnTheBoundary_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var visitStart = now.AddHours(2);
        ClientRescheduleWindow.CanClientCancel(now, visitStart, minHours: 2).Should().BeTrue();
    }

    [Fact]
    public void CanClientCancel_ZeroMinHours_AllowsRightUpToVisitStart()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ClientRescheduleWindow.CanClientCancel(now, now, minHours: 0).Should().BeTrue();
    }

    [Fact]
    public void CanClientCancel_VisitAlreadyInThePast_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var visitStart = now.AddHours(-1);
        ClientRescheduleWindow.CanClientCancel(now, visitStart, minHours: 2).Should().BeFalse();
    }
}
