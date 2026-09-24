using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// The one-time secret a person shares by opening a MAX deep link (ARCHITECTURE_CYCLE14.md §145.1,
/// R8). Pure — no clock, no DB, no I/O other than the CSPRNG — so it is covered by a plain unit test.
/// The payload itself is NEVER stored; only its SHA-256 (<see cref="Hash"/>) goes into
/// <c>PhoneVerificationSession.PayloadHash</c>, so a database dump cannot be used to open anyone's
/// pending session.
/// </summary>
public static class PayloadGenerator
{
    /// <summary>Versioning the format up front (§145.1) — a future payload shape change (longer, a
    /// different encoding) is then a new prefix, distinguishable from old links still in flight, rather
    /// than an ambiguous byte string.</summary>
    public const string Prefix = "v1.";

    private const int RandomByteCount = 32;

    /// <summary><c>О6</c>'s stated platform ceiling — the deep link's <c>start</c> parameter must never
    /// exceed this. <see cref="Generate"/>'s own output is 46 characters (3-char prefix + 43-char
    /// base64url of 32 bytes), comfortably under it; kept as an explicit constant so a future format
    /// change is checked against the real limit, not just "looks short enough".</summary>
    public const int MaxLength = 128;

    /// <summary>32 CSPRNG bytes, base64url-encoded (no padding), prefixed with <see cref="Prefix"/>.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(RandomByteCount);
        return Prefix + Base64UrlEncode(bytes);
    }

    /// <summary>SHA-256 of the payload, as lowercase hex — the only form of the payload this subsystem
    /// ever persists (<c>PhoneVerificationSession.PayloadHash</c>) or looks a session up by.</summary>
    public static string Hash(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
