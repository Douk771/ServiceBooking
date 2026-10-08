using FluentAssertions;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE39.md §39.5.3, A39-14 — the nights of a booking are released from ONE place (the releaser frees the sessions of services right after them).</summary>
public class StayBookingReleaserGuardTests
{
    [Fact]
    public void ReleaseBookingAsync_is_called_only_from_StayBookingReleaser()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ServiceBooking.sln"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "ServiceBooking.API"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !f.EndsWith("StayBookingReleaser.cs") && !f.EndsWith("HouseOccupancyWriter.cs"))
            .Where(f => File.ReadAllText(f).Contains("ReleaseBookingAsync(")).ToList();
        offenders.Should().BeEmpty();
    }
}
