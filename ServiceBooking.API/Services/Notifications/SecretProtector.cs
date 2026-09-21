using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Thrown whenever a channel's stored secret cannot be turned back into plaintext: wrong master key,
/// corrupted ciphertext, or a ciphertext copied from a different channel's row (the AAD binds it to
/// one <see cref="Guid"/>). Callers translate this into "channel needs reconnecting" (§26.4,
/// §30.1) rather than letting a raw <see cref="CryptographicException"/> or its message — which can
/// echo back attacker-controlled bytes — reach a log or an HTTP response.
/// </summary>
public sealed class ChannelSecretUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Encrypts/decrypts a WhatsApp channel's provider token (ARCHITECTURE_CYCLE4.md §24). Direct AES-GCM
/// over a single master key from <c>Notifications:EncryptionKey</c> (32 bytes, base64) — chosen over
/// ASP.NET Core Data Protection because Data Protection's key ring is ephemeral by default in a
/// container and would silently stop reading every previously-encrypted token on redeploy (§24 table).
///
/// Ciphertext format: <c>v1.&lt;keyId&gt;.&lt;base64(nonce12 ‖ tag16 ‖ ciphertext)&gt;</c>.
/// <c>keyId</c> is the first 8 hex characters of <see cref="ComputeKeyFingerprint"/> — the same value
/// <see cref="ChannelKeyFingerprint"/> persists for the whole key, letting an operator eyeball-match a
/// channel row's key id against the fingerprint file during a rotation. AAD is the channel id, so a
/// ciphertext copied from one channel's row into another's does not decrypt.
///
/// A fresh, cryptographically random nonce is generated on every single call to <see cref="Encrypt"/> —
/// nonce reuse under AES-GCM with the same key lets an attacker recover the authentication key and forge
/// ciphertexts, so this must never be memoized, derived from a counter, or reused across retries.
/// </summary>
public static class SecretProtector
{
    private const string FormatPrefix = "v1";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    /// <summary>SHA-256 of the raw 32-byte key, as 64 lowercase hex characters.</summary>
    public static string ComputeKeyFingerprint(byte[] keyBytes) =>
        Convert.ToHexString(SHA256.HashData(keyBytes)).ToLowerInvariant();

    /// <summary>First 8 hex characters of <see cref="ComputeKeyFingerprint"/> — embedded in ciphertexts.</summary>
    public static string ComputeKeyId(byte[] keyBytes) => ComputeKeyFingerprint(keyBytes)[..8];

    /// <summary>
    /// Decodes and validates <c>Notifications:EncryptionKey</c>. Throws <see cref="ArgumentException"/>
    /// (a configuration error, not a runtime secret-unavailable condition) when the value is missing,
    /// not valid base64, or does not decode to exactly 32 bytes.
    /// </summary>
    public static byte[] DecodeKey(string? keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new ArgumentException("Notifications:EncryptionKey is missing.", nameof(keyBase64));

        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Notifications:EncryptionKey is not valid base64.", nameof(keyBase64), ex);
        }

        if (key.Length != KeySizeBytes)
            throw new ArgumentException(
                $"Notifications:EncryptionKey must decode to exactly {KeySizeBytes} bytes, got {key.Length}.",
                nameof(keyBase64));

        return key;
    }

    /// <summary>Encrypts <paramref name="plaintext"/> for the given channel, using the master key.</summary>
    public static string Encrypt(string plaintext, string keyBase64, Guid channelId)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var key = DecodeKey(keyBase64);
        var keyId = ComputeKeyId(key);

        // Fresh nonce on EVERY call — see the class doc. RandomNumberGenerator, not Random.
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertextBytes = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];
        var aad = AssociatedData(channelId);

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertextBytes, tag, aad);
        }

        var payload = new byte[NonceSizeBytes + TagSizeBytes + ciphertextBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertextBytes, 0, payload, NonceSizeBytes + TagSizeBytes, ciphertextBytes.Length);

        return $"{FormatPrefix}.{keyId}.{Convert.ToBase64String(payload)}";
    }

    /// <summary>
    /// Decrypts a value produced by <see cref="Encrypt"/> for the same channel. Throws
    /// <see cref="ChannelSecretUnavailableException"/> — never a raw <see cref="CryptographicException"/>
    /// or a message containing key/ciphertext material — for every way this can legitimately fail at
    /// runtime: wrong master key (including after a lost/regenerated key, US-54 p.7), corrupted storage,
    /// or a ciphertext moved between channel rows.
    /// </summary>
    public static string Decrypt(string stored, string keyBase64, Guid channelId)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var parts = stored.Split('.', 3);
        if (parts.Length != 3 || parts[0] != FormatPrefix)
            throw new ChannelSecretUnavailableException(
                "Stored channel secret has an unrecognised format (expected 'v1.<keyId>.<payload>').");

        var key = DecodeKey(keyBase64);
        var expectedKeyId = ComputeKeyId(key);
        if (!string.Equals(parts[1], expectedKeyId, StringComparison.Ordinal))
            throw new ChannelSecretUnavailableException(
                "Stored channel secret was encrypted with a different master key than the one configured " +
                "now. The channel needs to be reconnected.");

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException ex)
        {
            throw new ChannelSecretUnavailableException("Stored channel secret payload is not valid base64.", ex);
        }

        if (payload.Length < NonceSizeBytes + TagSizeBytes)
            throw new ChannelSecretUnavailableException(
                "Stored channel secret payload is too short to contain a nonce and an authentication tag.");

        var nonce = payload[..NonceSizeBytes];
        var tag = payload[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = payload[(NonceSizeBytes + TagSizeBytes)..];
        var plaintext = new byte[ciphertext.Length];
        var aad = AssociatedData(channelId);

        try
        {
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        }
        catch (CryptographicException ex)
        {
            // AuthenticationTagMismatchException (a CryptographicException subtype) or any other GCM
            // failure — wrong key material after all, tampered ciphertext, or AAD mismatch (ciphertext
            // copied from a different channel's row). All three collapse to the same caller-facing
            // outcome: this channel needs to be reconnected.
            throw new ChannelSecretUnavailableException(
                "Stored channel secret could not be decrypted. The channel needs to be reconnected.", ex);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] AssociatedData(Guid channelId) => Encoding.UTF8.GetBytes(channelId.ToString());
}
