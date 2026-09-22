using System.Security.Cryptography;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE4.md §24.1: AES-GCM channel secret protection, no host, no file system, no network.
/// </summary>
public class SecretProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void EncryptThenDecrypt_SameKeyAndChannel_RoundTrips()
    {
        var key = NewKey();
        var channelId = Guid.NewGuid();

        var ciphertext = SecretProtector.Encrypt("super-secret-token", key, channelId);
        var plaintext = SecretProtector.Decrypt(ciphertext, key, channelId);

        plaintext.Should().Be("super-secret-token");
    }

    [Fact]
    public void Encrypt_TwoCallsSamePlaintext_ProduceDifferentCiphertexts()
    {
        // Nonce must be fresh every call — reuse under GCM is catastrophic (class doc). Equal ciphertexts
        // for equal plaintexts would be the observable symptom of a reused/derived nonce.
        var key = NewKey();
        var channelId = Guid.NewGuid();

        var first = SecretProtector.Encrypt("same-token", key, channelId);
        var second = SecretProtector.Encrypt("same-token", key, channelId);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Encrypt_ProducesExpectedFormat()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var key = Convert.ToBase64String(keyBytes);
        var expectedKeyId = SecretProtector.ComputeKeyId(keyBytes);

        var ciphertext = SecretProtector.Encrypt("x", key, Guid.NewGuid());

        var parts = ciphertext.Split('.', 3);
        parts.Should().HaveCount(3);
        parts[0].Should().Be("v1");
        parts[1].Should().Be(expectedKeyId);
        parts[1].Should().HaveLength(8);
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsChannelSecretUnavailable()
    {
        var channelId = Guid.NewGuid();
        var ciphertext = SecretProtector.Encrypt("token", NewKey(), channelId);

        var act = () => SecretProtector.Decrypt(ciphertext, NewKey(), channelId);

        act.Should().Throw<ChannelSecretUnavailableException>();
    }

    [Fact]
    public void Decrypt_WrongChannelId_ThrowsChannelSecretUnavailable()
    {
        // AAD binds the ciphertext to one channel — a ciphertext copied between channel rows in a dump
        // must not decrypt (§24.1).
        var key = NewKey();
        var ciphertext = SecretProtector.Encrypt("token", key, Guid.NewGuid());

        var act = () => SecretProtector.Decrypt(ciphertext, key, Guid.NewGuid());

        act.Should().Throw<ChannelSecretUnavailableException>();
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsChannelSecretUnavailable()
    {
        var key = NewKey();
        var channelId = Guid.NewGuid();
        var ciphertext = SecretProtector.Encrypt("token", key, channelId);

        // Flip a character deep in the payload (not the "v1." prefix, not the keyId segment).
        var tamperedPayload = ciphertext[^1] == 'A' ? "B" : "A";
        var tampered = ciphertext[..^1] + tamperedPayload;

        var act = () => SecretProtector.Decrypt(tampered, key, channelId);

        act.Should().Throw<ChannelSecretUnavailableException>();
    }

    [Theory]
    [InlineData("not-the-right-format")]
    [InlineData("v2.abcd1234.somepayload")]
    [InlineData("")]
    public void Decrypt_UnrecognisedFormat_ThrowsChannelSecretUnavailable(string malformed)
    {
        var act = () => SecretProtector.Decrypt(malformed, NewKey(), Guid.NewGuid());

        act.Should().Throw<ChannelSecretUnavailableException>();
    }

    [Fact]
    public void Decrypt_ExceptionMessage_NeverContainsKeyOrCiphertext()
    {
        // §24.3: three barriers against leaking a channel secret into a log/exception. This is the third
        // one — the adapter never lets a raw exception escape, but the message itself must also be safe
        // in case something upstream logs it anyway.
        var key = NewKey();
        var channelId = Guid.NewGuid();
        var ciphertext = SecretProtector.Encrypt("super-secret-token-value", key, channelId);

        Exception? caught = null;
        try
        {
            SecretProtector.Decrypt(ciphertext, NewKey(), channelId);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught!.Message.Should().NotContain(key);
        caught.Message.Should().NotContain("super-secret-token-value");
        caught.Message.Should().NotContain(ciphertext);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    public void DecodeKey_InvalidBase64_ThrowsArgumentException(string invalid)
    {
        var act = () => SecretProtector.DecodeKey(invalid);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecodeKey_WrongLength_ThrowsArgumentException()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var act = () => SecretProtector.DecodeKey(shortKey);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeKeyFingerprint_IsDeterministic()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);

        var first = SecretProtector.ComputeKeyFingerprint(keyBytes);
        var second = SecretProtector.ComputeKeyFingerprint(keyBytes);

        first.Should().Be(second);
        first.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeKeyId_IsFirstEightCharsOfFingerprint()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);

        var fingerprint = SecretProtector.ComputeKeyFingerprint(keyBytes);
        var keyId = SecretProtector.ComputeKeyId(keyBytes);

        keyId.Should().Be(fingerprint[..8]);
    }

    // ── String-AAD overload (T5-B6, ARCHITECTURE_CYCLE5.md §48.1 — HealthNoteProtector's foundation) ──

    [Fact]
    public void EncryptThenDecrypt_SameKeyAndAssociatedData_RoundTrips_StringOverload()
    {
        var key = NewKey();
        var aad = "health:11111111-1111-1111-1111-111111111111:phone:79991234567";

        var ciphertext = SecretProtector.Encrypt("аллергия на аммиак", key, aad);
        var plaintext = SecretProtector.Decrypt(ciphertext, key, aad);

        plaintext.Should().Be("аллергия на аммиак");
    }

    [Fact]
    public void Decrypt_StringOverload_DifferentAssociatedData_Throws()
    {
        // The whole point of AAD (ARCHITECTURE_CYCLE5.md §48.1): a ciphertext moved between rows —
        // different company, different subject — must not decrypt, even with the right key.
        var key = NewKey();
        var ciphertext = SecretProtector.Encrypt("secret", key, "health:company-a:phone:1");

        var act = () => SecretProtector.Decrypt(ciphertext, key, "health:company-b:phone:1");

        act.Should().Throw<ChannelSecretUnavailableException>();
    }

    [Fact]
    public void GuidOverload_And_StringOverload_ProduceCompatibleFormats()
    {
        // Encrypt(Guid) delegates to Encrypt(string) with channelId.ToString() — this pins that the
        // delegation actually happened (same keyId/prefix shape), not two diverging implementations.
        var key = NewKey();
        var channelId = Guid.NewGuid();

        var viaGuid = SecretProtector.Encrypt("token", key, channelId);
        var plaintext = SecretProtector.Decrypt(viaGuid, key, channelId.ToString());

        plaintext.Should().Be("token");
    }
}
