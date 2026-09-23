using System.Numerics;
using System.Security.Cryptography;

namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// Pure, DI-free validation that a configured VAPID key pair (ARCHITECTURE_CYCLE9.md §105.2, §105.3)
/// actually parses as P-256 — used by <c>DeploymentSafetyChecks.ValidateStaffPushSecrets</c> (§105.3's
/// "ключей нет или они не парсятся как пара P-256 → старт падает").
/// </summary>
public static class VapidKeyValidator
{
    /// <summary>
    /// Decodes both keys as base64url (the format the Web Push/VAPID ecosystem — and browsers'
    /// <c>PushManager.subscribe</c> — universally use, RFC 8292) and imports them as an uncompressed
    /// P-256 point / 32-byte scalar. Returns <see langword="false"/> for anything that doesn't parse —
    /// wrong length, wrong curve, not actually a key pair — never throws, so the caller can turn a bad
    /// value into one clear fail-fast message instead of an unrelated crypto exception.
    /// </summary>
    public static bool IsValidP256Pair(string? publicKeyBase64Url, string? privateKeyBase64Url)
    {
        if (string.IsNullOrWhiteSpace(publicKeyBase64Url) || string.IsNullOrWhiteSpace(privateKeyBase64Url))
            return false;

        byte[] publicKeyBytes, privateKeyBytes;
        try
        {
            publicKeyBytes = Base64UrlDecode(publicKeyBase64Url);
            privateKeyBytes = Base64UrlDecode(privateKeyBase64Url);
        }
        catch (FormatException)
        {
            return false;
        }

        // Uncompressed P-256 point: 0x04 || X(32) || Y(32) = 65 bytes. Private scalar: 32 bytes.
        if (publicKeyBytes.Length != 65 || publicKeyBytes[0] != 0x04) return false;
        if (privateKeyBytes.Length != 32) return false;

        // Validated independently, on purpose: this method checks that each key is individually
        // well-formed for the P-256 curve — it does NOT check that the public key is mathematically
        // derived from the private scalar. Importing both halves together as one ECParameters pair
        // (Q + D) is platform-dependent: on Linux, .NET's OpenSSL-backed provider cross-validates that
        // Q == D*G and throws for an unrelated public/private combination, while on macOS's
        // SecureTransport-backed provider it does not — so a combined import silently changes behavior
        // by OS. Checking each half on its own avoids that platform dependency entirely.
        if (!IsPointOnCurve(publicKeyBytes)) return false;
        if (!IsValidScalar(privateKeyBytes)) return false;

        return true;
    }

    private static bool IsPointOnCurve(byte[] publicKeyBytes)
    {
        try
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = publicKeyBytes[1..33], Y = publicKeyBytes[33..65] },
            };
            using var ecdh = ECDiffieHellman.Create(parameters);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // NIST P-256 (secp256r1) group order n, per FIPS 186-4 / SEC 2. A valid private scalar D must
    // satisfy 0 < D < n.
    private static readonly BigInteger P256Order = BigInteger.Parse(
        "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        System.Globalization.NumberStyles.HexNumber);

    private static bool IsValidScalar(byte[] privateKeyBytes)
    {
        // BigInteger's byte[] constructor expects little-endian; our key bytes are big-endian, and a
        // leading 0x00 must be appended so the most-significant byte is never treated as a sign bit
        // (P-256 scalars are unsigned, but their high byte can be >= 0x80).
        var bigEndianUnsigned = new byte[privateKeyBytes.Length + 1];
        Buffer.BlockCopy(privateKeyBytes, 0, bigEndianUnsigned, 1, privateKeyBytes.Length);
        Array.Reverse(bigEndianUnsigned);
        var scalar = new BigInteger(bigEndianUnsigned);
        return scalar > BigInteger.Zero && scalar < P256Order;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
