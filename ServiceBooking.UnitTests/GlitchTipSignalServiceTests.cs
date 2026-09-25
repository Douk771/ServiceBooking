using FluentAssertions;
using ServiceBooking.API.Services.Signals;

namespace ServiceBooking.UnitTests;

/// <summary>
/// TD-03-quater (ARCHITECTURE_CYCLE16.md §9 п.9) — <see cref="GlitchTipSignalService.TryParseDsn"/> is
/// the pure part of the signal service split out specifically so it can be exercised without any HTTP
/// dependency. The actual send path (real POST over the network) is intentionally NOT unit-tested here —
/// that would require either a real HTTP call or a mocked handler wired through DI, which is exactly the
/// kind of thing this team's rules push to a functional/integration test instead.
/// </summary>
public class GlitchTipSignalServiceTests
{
    [Fact]
    public void TryParseDsn_ValidDsn_ReturnsStoreUrlAndAuthHeader()
    {
        var ok = GlitchTipSignalService.TryParseDsn(
            "https://abc123@glitchtip.example.com/7", out var url, out var authHeader);

        ok.Should().BeTrue();
        url.Should().Be("https://glitchtip.example.com/api/7/store/");
        authHeader.Should().Be("Sentry sentry_version=7, sentry_key=abc123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseDsn_NoDsnConfigured_ReturnsFalse(string? dsn)
    {
        GlitchTipSignalService.TryParseDsn(dsn, out var url, out var authHeader).Should().BeFalse();
        url.Should().BeEmpty();
        authHeader.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("https://glitchtip.example.com/7")] // missing key@
    [InlineData("https://abc123@glitchtip.example.com/")] // missing project id
    [InlineData("https://abc123@glitchtip.example.com/not-a-number")]
    [InlineData("ftp://abc123@glitchtip.example.com/7")] // wrong scheme
    public void TryParseDsn_MalformedDsn_ReturnsFalseAndNeverThrows(string dsn)
    {
        var act = () => GlitchTipSignalService.TryParseDsn(dsn, out _, out _);
        act.Should().NotThrow();
        GlitchTipSignalService.TryParseDsn(dsn, out var url, out var authHeader).Should().BeFalse();
        url.Should().BeEmpty();
        authHeader.Should().BeEmpty();
    }

    [Fact]
    public void TryParseDsn_TrimsWhitespaceAroundTheDsn()
    {
        var ok = GlitchTipSignalService.TryParseDsn(
            "  https://key@host.example.com/42  ", out var url, out _);

        ok.Should().BeTrue();
        url.Should().Be("https://host.example.com/api/42/store/");
    }
}
