using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

public class TokenServiceTests
{
    // ── HashSecurityStamp ────────────────────────────────────────────────────

    [Fact]
    public void HashSecurityStamp_SameStamp_ProducesSameHash()
    {
        var hash1 = TokenService.HashSecurityStamp("ABCDEFGH12345");
        var hash2 = TokenService.HashSecurityStamp("ABCDEFGH12345");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashSecurityStamp_DifferentStamps_ProduceDifferentHashes()
    {
        // A stamp rotation (password/phone change) must change the hash baked into future tokens,
        // otherwise Program.cs's OnTokenValidated comparison would never invalidate anything.
        var hash1 = TokenService.HashSecurityStamp("ABCDEFGH12345");
        var hash2 = TokenService.HashSecurityStamp("ZYXWVUTS98765");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashSecurityStamp_NeverReturnsTheRawStampValue()
    {
        // The whole point of hashing: the internal identity-rotation token must not appear verbatim in
        // whatever gets embedded in the JWT payload.
        const string stamp = "ABCDEFGH12345";

        var hash = TokenService.HashSecurityStamp(stamp);

        hash.Should().NotBe(stamp);
        hash.Should().NotContain(stamp);
    }

    [Fact]
    public void HashSecurityStamp_IsShortAndLowercaseHex()
    {
        var hash = TokenService.HashSecurityStamp("some-stamp-value");

        hash.Should().HaveLength(8);
        hash.Should().MatchRegex("^[0-9a-f]{8}$");
    }

    [Fact]
    public void HashSecurityStamp_Null_DoesNotThrow_AndDiffersFromEmptyStringInput()
    {
        // A brand-new AppUser can theoretically have a null SecurityStamp before Identity assigns one;
        // this must degrade gracefully rather than NullReferenceException, and still hash consistently
        // with how the same null-coalesced empty string is treated elsewhere.
        var hashFromNull = TokenService.HashSecurityStamp(null);
        var hashFromEmpty = TokenService.HashSecurityStamp("");

        hashFromNull.Should().Be(hashFromEmpty);
    }
}
