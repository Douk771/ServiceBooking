using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services;

/// <summary>
/// Builds and reads the signed, tableless unsubscribe link token (ARCHITECTURE_CYCLE4.md §31.4,
/// US-33): <c>token = Base64Url(phoneBytes ‖ HMAC-SHA256(phoneBytes, key)[..16])</c>. No token table —
/// the phone number travels inside the token itself, the truncated HMAC is what stops it being forged
/// or enumerated. Deliberately keyed by <c>Notifications:UnsubscribeKey</c>, NOT
/// <c>Notifications:WebhookToken</c> (which leaves the server, handed to the provider as
/// <c>webhookUrlToken</c>) — this key must never leave the server either, since anyone holding it could
/// mint an unsubscribe link for an arbitrary phone number.
///
/// Placed outside <c>Services/Notifications/</c> deliberately (this cycle's file-ownership split
/// between two backend developers) even though the architecture doc's file map lists it there.
/// </summary>
public static class UnsubscribeTokens
{
    private const int SignatureBytes = 16;

    /// <summary>Builds a token for <paramref name="canonicalPhone"/>. <paramref name="key"/> is the raw
    /// UTF-8 bytes of <c>Notifications:UnsubscribeKey</c> — callers own turning the configured string
    /// into bytes so this class doesn't need to know the key's own encoding convention.</summary>
    public static string Build(string canonicalPhone, byte[] key)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonicalPhone);
        var phoneBytes = Encoding.UTF8.GetBytes(canonicalPhone);
        var signature = ComputeSignature(phoneBytes, key);

        var payload = new byte[phoneBytes.Length + SignatureBytes];
        Buffer.BlockCopy(phoneBytes, 0, payload, 0, phoneBytes.Length);
        Buffer.BlockCopy(signature, 0, payload, phoneBytes.Length, SignatureBytes);

        return Base64UrlEncode(payload);
    }

    /// <summary>
    /// Verifies the signature and extracts the phone number. Returns <see langword="false"/> for any
    /// malformed or tampered token (wrong base64url, too short to contain a signature, or a signature
    /// that doesn't match) — the caller (NotificationsController) turns that into a 404, deliberately
    /// indistinguishable from "no such token", so brute-forcing phone numbers through this endpoint
    /// gains nothing observable.
    /// </summary>
    public static bool TryRead(string? token, byte[] key, out string canonicalPhone)
    {
        canonicalPhone = string.Empty;
        if (string.IsNullOrEmpty(token)) return false;

        byte[] payload;
        try
        {
            payload = Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length <= SignatureBytes) return false;

        var phoneBytes = payload[..^SignatureBytes];
        var signature = payload[^SignatureBytes..];
        var expectedSignature = ComputeSignature(phoneBytes, key);

        if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature)) return false;

        canonicalPhone = Encoding.UTF8.GetString(phoneBytes);
        return true;
    }

    private static byte[] ComputeSignature(byte[] phoneBytes, byte[] key) =>
        HMACSHA256.HashData(key, phoneBytes)[..SignatureBytes];

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: throw new FormatException("Invalid base64url length.");
        }
        return Convert.FromBase64String(padded);
    }
}
