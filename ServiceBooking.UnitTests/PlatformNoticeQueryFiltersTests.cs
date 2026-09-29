using FluentAssertions;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class PlatformNoticeQueryFiltersTests
{
    [Fact]
    public void TryParseKind_Null_ReturnsNoFilter()
    {
        var ok = PlatformNoticeQueryFilters.TryParseKind(null, out var kind);

        ok.Should().BeTrue();
        kind.Should().BeNull();
    }

    [Fact]
    public void TryParseKind_Empty_ReturnsNoFilter()
    {
        var ok = PlatformNoticeQueryFilters.TryParseKind("", out var kind);

        ok.Should().BeTrue();
        kind.Should().BeNull();
    }

    [Fact]
    public void TryParseKind_Whitespace_ReturnsNoFilter()
    {
        var ok = PlatformNoticeQueryFilters.TryParseKind("   ", out var kind);

        ok.Should().BeTrue();
        kind.Should().BeNull();
    }

    [Theory]
    [InlineData("PriceChange", PlatformNoticeKind.PriceChange)]
    [InlineData("priceChange", PlatformNoticeKind.PriceChange)]
    [InlineData("PHOTOREMOVED", PlatformNoticeKind.PhotoRemoved)]
    [InlineData("Other", PlatformNoticeKind.Other)]
    public void TryParseKind_KnownName_AnyCase_ReturnsKind(string value, PlatformNoticeKind expected)
    {
        var ok = PlatformNoticeQueryFilters.TryParseKind(value, out var kind);

        ok.Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Fact]
    public void TryParseKind_Garbage_ReturnsFalse()
    {
        // The contract-check finding this closes: this used to never reach any of our own code at all
        // ([ApiController] rejected it before the action ran) — the caller got a generic framework
        // sentence instead of a message naming `kind`.
        var ok = PlatformNoticeQueryFilters.TryParseKind("NotARealKind", out var kind);

        ok.Should().BeFalse();
        kind.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("99")]
    public void TryParseKind_NumericValue_ReturnsFalse(string value)
    {
        // Deliberately stricter than BookingFilters.TryParseClientStatus: this filter is new in cycle
        // 20, so unlike BookingStatus's query filter there is no existing caller that could depend on
        // the numeric-ordinal form, and accepting it would silently reopen the hole
        // LegalController.GetDocument's own {type} parsing was written to close (Enum.TryParse maps ANY
        // integer literal onto the enum's underlying value, defined or not, named or not).
        var ok = PlatformNoticeQueryFilters.TryParseKind(value, out var kind);

        ok.Should().BeFalse();
        kind.Should().BeNull();
    }
}
