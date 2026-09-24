using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// Check 1 of 2 (ARCHITECTURE_CYCLE14.md §147.1) — proves the <c>contact</c> attachment's <c>vcf_info</c>
/// was not tampered with in transit: MAX's own platform signs it with HMAC-SHA256 keyed by the bot's own
/// token. This is the ONLY place the bot token is ever used as a cryptographic key (everywhere else it is
/// a bearer credential in an <c>Authorization</c> header) — if a live-bot recon ever shows a different
/// signing scheme, only this file and its tests change.
/// </summary>
public static class MaxContactSignature
{
    /// <summary>
    /// Verifies <paramref name="hash"/> against <c>HMAC-SHA256(botToken, vcfInfo)</c>, in constant time.
    /// An empty/missing hash or an empty vcfInfo is a REJECTION (<c>false</c>), never treated as "nothing
    /// to check" (§147.1's explicit "а не «нечего проверять»").
    /// </summary>
    /// <param name="vcfInfo">Raw vCard text the signature was computed over.</param>
    /// <param name="hash">Hex or base64 — the format the platform actually sends is unconfirmed until a
    /// live recon, so both are accepted and normalized before comparison (§147.1).</param>
    /// <param name="botToken">The signing key — this bot's own API token.</param>
    public static bool Verify(string? vcfInfo, string? hash, string botToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);

        if (string.IsNullOrEmpty(vcfInfo) || string.IsNullOrEmpty(hash)) return false;

        if (!TryDecode(hash, out var presented)) return false;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(botToken), Encoding.UTF8.GetBytes(vcfInfo));

        // Constant-time: a signature check must not leak "how many leading bytes matched" through timing.
        return presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    private static bool TryDecode(string hash, out byte[] bytes)
    {
        var trimmed = hash.Trim();

        if (IsHex(trimmed))
        {
            bytes = Convert.FromHexString(trimmed);
            return true;
        }

        try
        {
            bytes = Convert.FromBase64String(trimmed);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool IsHex(string value) =>
        value.Length > 0 && value.Length % 2 == 0 && value.All(Uri.IsHexDigit);
}
