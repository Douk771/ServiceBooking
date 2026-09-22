using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE5.md §44/§45 — the {clientKey} route segment format.</summary>
public class ClientKeyTests
{
    [Theory]
    [InlineData("phone:79991234567", true)]
    [InlineData("phone:", true)]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6", false)]
    [InlineData("", false)]
    public void IsPhone_DetectsThePrefix(string clientKey, bool expected)
    {
        ClientKey.IsPhone(clientKey).Should().Be(expected);
    }

    [Fact]
    public void ExtractPhone_StripsThePrefix()
    {
        ClientKey.ExtractPhone("phone:79991234567").Should().Be("79991234567");
    }

    [Fact]
    public void ExtractPhone_EmptyAfterPrefix_ReturnsEmptyString()
    {
        ClientKey.ExtractPhone("phone:").Should().BeEmpty();
    }
}
