using FluentAssertions;
using ServiceBooking.API.DTOs.Common;

namespace ServiceBooking.UnitTests;

public class PaginationTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Normalize_Page_ClampsBelowOneToOne(int? page, int expected) =>
        Pagination.Normalize(page, null).Page.Should().Be(expected);

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(20, 20)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void Normalize_PageSize_DefaultsAndKeepsWithinBounds(int? pageSize, int expected) =>
        Pagination.Normalize(null, pageSize).PageSize.Should().Be(expected);

    [Fact]
    public void Normalize_PageSizeAboveMax_IsClampedNotRejected()
    {
        // US-49 p.6: silently clamped to 100, not a 400 — an oversized pageSize is not a client error.
        var (_, pageSize) = Pagination.Normalize(1, 1000);
        pageSize.Should().Be(100);
    }

    [Fact]
    public void Create_HasNext_TrueWhenMoreRowsRemain()
    {
        var result = Pagination.Create(new[] { 1, 2, 3 }, page: 1, pageSize: 20, total: 137);
        result.HasNext.Should().BeTrue();
    }

    [Fact]
    public void Create_HasNext_FalseOnTheLastPage()
    {
        var result = Pagination.Create(new[] { 1 }, page: 7, pageSize: 20, total: 137);
        result.HasNext.Should().BeFalse();
    }

    [Fact]
    public void Create_HasNext_FalseWhenExactlyAtTheBoundary()
    {
        // page * pageSize == total means this page ends exactly on the last row — nothing left after it.
        var result = Pagination.Create(Array.Empty<int>(), page: 5, pageSize: 20, total: 100);
        result.HasNext.Should().BeFalse();
    }

    [Fact]
    public void Create_PropagatesPageAndPageSizeAndTotalUnchanged()
    {
        var result = Pagination.Create(new[] { "a" }, page: 3, pageSize: 10, total: 42);
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.Total.Should().Be(42);
        result.Items.Should().Equal("a");
    }
}
