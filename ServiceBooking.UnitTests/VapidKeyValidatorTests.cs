using System.Security.Cryptography;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications.WebPush;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE9.md §105.3 (Q15, R12) — "ключей нет или они не парсятся как пара P-256
/// → старт падает". Pure, DI-free, no network/DB — generates real P-256 pairs with
/// <see cref="ECDiffieHellman"/> so the "valid" cases are genuinely valid, not just well-shaped.</summary>
public class VapidKeyValidatorTests
{
    private static (string PublicKey, string PrivateKey) GenerateValidPair()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdh.ExportParameters(includePrivateParameters: true);

        var publicKeyBytes = new byte[65];
        publicKeyBytes[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X!, 0, publicKeyBytes, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y!, 0, publicKeyBytes, 33, 32);

        return (Base64UrlEncode(publicKeyBytes), Base64UrlEncode(parameters.D!));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [Fact]
    public void IsValidP256Pair_RealGeneratedPair_ReturnsTrue()
    {
        var (publicKey, privateKey) = GenerateValidPair();
        VapidKeyValidator.IsValidP256Pair(publicKey, privateKey).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "somevalue")]
    [InlineData("somevalue", null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void IsValidP256Pair_MissingEitherKey_ReturnsFalse(string? publicKey, string? privateKey) =>
        VapidKeyValidator.IsValidP256Pair(publicKey, privateKey).Should().BeFalse();

    [Fact]
    public void IsValidP256Pair_NotBase64_ReturnsFalse() =>
        VapidKeyValidator.IsValidP256Pair("not-!!valid-base64-url-@@@", "also-not-valid-###")
            .Should().BeFalse();

    [Fact]
    public void IsValidP256Pair_WrongLengthPublicKey_ReturnsFalse()
    {
        var (_, privateKey) = GenerateValidPair();
        var tooShortPublicKey = Base64UrlEncode(new byte[10]);
        VapidKeyValidator.IsValidP256Pair(tooShortPublicKey, privateKey).Should().BeFalse();
    }

    [Fact]
    public void IsValidP256Pair_WrongLengthPrivateKey_ReturnsFalse()
    {
        var (publicKey, _) = GenerateValidPair();
        var tooShortPrivateKey = Base64UrlEncode(new byte[10]);
        VapidKeyValidator.IsValidP256Pair(publicKey, tooShortPrivateKey).Should().BeFalse();
    }

    [Fact]
    public void IsValidP256Pair_PublicKeyMissingUncompressedPrefix_ReturnsFalse()
    {
        var (publicKey, privateKey) = GenerateValidPair();
        var raw = Convert.FromBase64String(publicKey.Replace('-', '+').Replace('_', '/').PadRight(publicKey.Length + (4 - publicKey.Length % 4) % 4, '='));
        raw[0] = 0x02; // compressed-point prefix, not the 0x04 this validator requires
        VapidKeyValidator.IsValidP256Pair(Base64UrlEncode(raw), privateKey).Should().BeFalse();
    }

    [Fact]
    public void IsValidP256Pair_MismatchedKeysFromTwoUnrelatedPairs_StillReturnsTrue()
    {
        // Deliberate documentation of the validator's contract: it checks that EACH key is individually
        // well-formed for the P-256 curve (right length, right curve, right prefix, scalar in range) —
        // it does NOT check that the public key is mathematically derived from the private scalar.
        // §105.3's own requirement is "не парсятся как пара P-256", not "cryptographically matched", so
        // accepting an unrelated public/private combination is within scope, not a bug.
        //
        // Fixed vectors, not freshly generated ones: combined ECParameters import (Q + D together) used
        // to be platform-dependent — OpenSSL on Linux cross-validates Q == D*G and rejects a mismatched
        // pair, while macOS's provider didn't, so this exact test flipped from green to red depending on
        // CI's OS even though nothing about the validator's *contract* changed. The validator now checks
        // each key on its own, so this no longer depends on which random bytes came out of key
        // generation, but pinning the vectors keeps that property locked in going forward.
        const string publicKeyA = "BDcskJOXa8Oq5rFyIBZhBj6LuaQJJXuV4b9HN_y59ftejEIXFkOQGmGhj5RrXdVAYxblpXrJurAVvR5IcCpbnYE";
        const string privateKeyB = "SEa2FMlEi2xy65Phzg8si13FLk0t0u8BptcU8_xJUQA";
        VapidKeyValidator.IsValidP256Pair(publicKeyA, privateKeyB).Should().BeTrue();
    }
}
