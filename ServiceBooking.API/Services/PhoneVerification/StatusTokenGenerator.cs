using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// The second half of a session's poll key (ARCHITECTURE_CYCLE14.md §142.1, §163). Returned to the
/// caller exactly once, at session creation; only its SHA-256 is stored
/// (<c>PhoneVerificationSession.StatusTokenHash</c>) — polling requires BOTH <c>sessionId</c> and this
/// token, so a session id alone (which appears in a URL, and could leak via a referrer header or a
/// screenshot) proves nothing on its own.
/// </summary>
public static class StatusTokenGenerator
{
    private const int RandomByteCount = 32;

    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(RandomByteCount))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
