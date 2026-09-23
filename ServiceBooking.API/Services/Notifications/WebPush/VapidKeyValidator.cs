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

        try
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = publicKeyBytes[1..33], Y = publicKeyBytes[33..65] },
                D = privateKeyBytes,
            };
            using var ecdh = ECDiffieHellman.Create(parameters);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
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
