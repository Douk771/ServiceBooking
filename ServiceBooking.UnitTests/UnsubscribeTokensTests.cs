using System.Text;
using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §31.4 — signed, tableless unsubscribe link token.</summary>
public class UnsubscribeTokensTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("test-unsubscribe-key-0123456789");
    private static readonly byte[] OtherKey = Encoding.UTF8.GetBytes("a-completely-different-key-here");

    [Fact]
    public void BuildThenTryRead_RoundTripsThePhoneNumber()
    {
        var token = UnsubscribeTokens.Build("79991234567", Key);

        UnsubscribeTokens.TryRead(token, Key, out var phone).Should().BeTrue();
        phone.Should().Be("79991234567");
    }

    [Fact]
    public void TryRead_WrongKey_Fails()
    {
        var token = UnsubscribeTokens.Build("79991234567", Key);

        UnsubscribeTokens.TryRead(token, OtherKey, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_TamperedToken_Fails()
    {
        var token = UnsubscribeTokens.Build("79991234567", Key);
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        UnsubscribeTokens.TryRead(tampered, Key, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-valid-token-at-all-!!!")]
    [InlineData("YQ")] // valid base64url but far too short to contain a signature
    public void TryRead_MalformedToken_FailsWithoutThrowing(string? token)
    {
        var act = () => UnsubscribeTokens.TryRead(token, Key, out _);
        act.Should().NotThrow();
        UnsubscribeTokens.TryRead(token, Key, out _).Should().BeFalse();
    }

    [Fact]
    public void Build_DifferentPhones_ProduceDifferentTokens()
    {
        var tokenA = UnsubscribeTokens.Build("79991234567", Key);
        var tokenB = UnsubscribeTokens.Build("79997654321", Key);

        tokenA.Should().NotBe(tokenB);
    }
}
