using FluentAssertions;
using ServiceBooking.TestKit;
using Xunit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// The random-order test orderer prints "replay this exact order with SEED=N" on every run. That
/// promise only holds if the per-class offset is identical in a LATER process — exactly what
/// string.GetHashCode cannot give, since .NET randomizes string hashing per process with no way to
/// disable it. These tests pin the replacement to literal values, so a future refactor that reaches
/// back for GetHashCode (or changes the algorithm) fails here instead of quietly breaking replay.
/// </summary>
public class StableHashTests
{
    [Theory]
    [InlineData("", -2128831035)]
    [InlineData("a", -468965076)]
    [InlineData("CompaniesTests", -1294727315)]
    public void OfString_MatchesKnownFnv1aValues(string input, int expected)
    {
        StableHash.OfString(input).Should().Be(expected);
    }

    [Fact]
    public void OfString_IsDeterministicWithinThisProcess()
    {
        var first = StableHash.OfString("ServiceBooking.Tests.Tests.BookingsTests");
        var second = StableHash.OfString("ServiceBooking.Tests.Tests.BookingsTests");

        second.Should().Be(first);
    }

    [Fact]
    public void OfString_SeparatesClassesThatDifferByOneCharacter()
    {
        var a = StableHash.OfString("ServiceBooking.Tests.Tests.AdminTests");
        var b = StableHash.OfString("ServiceBooking.Tests.Tests.AdmimTests");

        b.Should().NotBe(a);
    }
}
