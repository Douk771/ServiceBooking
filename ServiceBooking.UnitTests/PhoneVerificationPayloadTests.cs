using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE14.md §144.3, §156.1 — payload length (≤128, О6), unpredictability, and
/// the hash relationship (same input → same hash; different input → different hash; the payload itself
/// is never recoverable from the hash).</summary>
public class PhoneVerificationPayloadTests
{
    [Fact]
    public void Generate_StaysWithinPlatformCeiling()
    {
        var payload = PayloadGenerator.Generate();
        payload.Length.Should().BeLessOrEqualTo(PayloadGenerator.MaxLength);
        payload.Should().StartWith(PayloadGenerator.Prefix);
    }

    [Fact]
    public void Generate_ProducesUnpredictableValues()
    {
        var payloads = Enumerable.Range(0, 200).Select(_ => PayloadGenerator.Generate()).ToList();
        payloads.Distinct().Should().HaveCount(200);
    }

    [Fact]
    public void Hash_IsDeterministicForTheSameInput()
    {
        var payload = PayloadGenerator.Generate();
        PayloadGenerator.Hash(payload).Should().Be(PayloadGenerator.Hash(payload));
    }

    [Fact]
    public void Hash_DiffersForDifferentInputs()
    {
        var a = PayloadGenerator.Generate();
        var b = PayloadGenerator.Generate();
        PayloadGenerator.Hash(a).Should().NotBe(PayloadGenerator.Hash(b));
    }

    [Fact]
    public void Hash_IsLowercaseHexSha256Length()
    {
        var hash = PayloadGenerator.Hash(PayloadGenerator.Generate());
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}

public class StatusTokenGeneratorTests
{
    [Fact]
    public void Generate_ProducesUnpredictableValues()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => StatusTokenGenerator.Generate()).ToList();
        tokens.Distinct().Should().HaveCount(200);
    }

    [Fact]
    public void Hash_IsDeterministicAndLowercaseHex()
    {
        var token = StatusTokenGenerator.Generate();
        var hash1 = StatusTokenGenerator.Hash(token);
        var hash2 = StatusTokenGenerator.Hash(token);

        hash1.Should().Be(hash2);
        hash1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Hash_DiffersForDifferentTokens() =>
        StatusTokenGenerator.Hash(StatusTokenGenerator.Generate())
            .Should().NotBe(StatusTokenGenerator.Hash(StatusTokenGenerator.Generate()));
}
