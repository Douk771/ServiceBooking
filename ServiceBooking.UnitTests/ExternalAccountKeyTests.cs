using System.Security.Cryptography;
using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE12.md §142.3, §156.1 — determinism (same account id + same key → same
/// key), and that either a different account id OR a different HMAC key produces a different result
/// (the whole point of §147.5's ceiling relying on equality search).</summary>
public class ExternalAccountKeyTests
{
    private static string RandomKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Compute_IsDeterministicForTheSameAccountAndKey()
    {
        var key = RandomKeyBase64();
        ExternalAccountKey.Compute(key, "12345").Should().Be(ExternalAccountKey.Compute(key, "12345"));
    }

    [Fact]
    public void Compute_DiffersForDifferentAccountIds()
    {
        var key = RandomKeyBase64();
        ExternalAccountKey.Compute(key, "12345").Should().NotBe(ExternalAccountKey.Compute(key, "67890"));
    }

    [Fact]
    public void Compute_DiffersForDifferentKeys()
    {
        var accountId = "12345";
        ExternalAccountKey.Compute(RandomKeyBase64(), accountId)
            .Should().NotBe(ExternalAccountKey.Compute(RandomKeyBase64(), accountId));
    }

    [Fact]
    public void Compute_IsLowercaseHexSha256Length() =>
        ExternalAccountKey.Compute(RandomKeyBase64(), "12345").Should().MatchRegex("^[0-9a-f]{64}$");

    [Fact]
    public void Compute_EmptyKey_Throws()
    {
        var act = () => ExternalAccountKey.Compute("", "12345");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compute_EmptyAccountId_Throws()
    {
        var act = () => ExternalAccountKey.Compute(RandomKeyBase64(), "");
        act.Should().Throw<ArgumentException>();
    }
}
