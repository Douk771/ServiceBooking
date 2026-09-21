using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class PhotoQuotaTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    // ── SixMonths ────────────────────────────────────────────────────────────

    [Fact]
    public void IsExpired_SixMonths_PhotoOlderThanSixMonths_IsExpired()
    {
        var createdAt = Now.AddMonths(-6).AddDays(-1);
        PhotoQuota.IsExpired(createdAt, PhotoRetention.SixMonths, Now).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_SixMonths_PhotoWithinSixMonths_IsNotExpired()
    {
        var createdAt = Now.AddMonths(-3);
        PhotoQuota.IsExpired(createdAt, PhotoRetention.SixMonths, Now).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_SixMonths_ExactlyOnTheBoundary_IsNotExpired()
    {
        // The cutoff itself is the boundary: a photo created EXACTLY at now-6months is not (yet) older
        // than the cutoff — IsExpired uses a strict "<" so the boundary instant survives one more pass.
        var createdAt = Now.AddMonths(-6);
        PhotoQuota.IsExpired(createdAt, PhotoRetention.SixMonths, Now).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_SixMonths_OneMillisecondPastTheBoundary_IsExpired()
    {
        var createdAt = Now.AddMonths(-6).AddMilliseconds(-1);
        PhotoQuota.IsExpired(createdAt, PhotoRetention.SixMonths, Now).Should().BeTrue();
    }

    // ── TwelveMonths ─────────────────────────────────────────────────────────

    [Fact]
    public void IsExpired_TwelveMonths_PhotoOlderThanTwelveMonths_IsExpired()
    {
        var createdAt = Now.AddMonths(-12).AddDays(-1);
        PhotoQuota.IsExpired(createdAt, PhotoRetention.TwelveMonths, Now).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_TwelveMonths_PhotoWithinTwelveMonths_IsNotExpired()
    {
        var createdAt = Now.AddMonths(-6); // expired under SixMonths, not under TwelveMonths
        PhotoQuota.IsExpired(createdAt, PhotoRetention.TwelveMonths, Now).Should().BeFalse();
    }

    // CYCLE5-BREAKING (ARCHITECTURE_CYCLE5.md §44.7, §59.1, Q-L6): `PhotoRetention.Forever` is REMOVED,
    // not renamed — "kept forever" cannot be a retention period for personal data, so there is no
    // replacement behavior to assert here. IsExpired_Forever_VeryOldPhoto_IsNeverExpired and
    // CutoffUtc_Forever_Throws (both formerly here) tested a case that no longer exists; removed rather
    // than rewritten, since §55.3's "rewrite under new behavior, keep the name" only applies where new
    // behavior exists to describe.

    [Theory]
    [InlineData(PhotoRetention.SixMonths, -6)]
    [InlineData(PhotoRetention.TwelveMonths, -12)]
    public void CutoffUtc_MatchesTheNamedNumberOfMonths(PhotoRetention retention, int expectedMonthOffset)
    {
        PhotoQuota.CutoffUtc(retention, Now).Should().Be(Now.AddMonths(expectedMonthOffset));
    }
}
