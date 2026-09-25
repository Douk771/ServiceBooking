using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 18, Д6/Д14 (ARCHITECTURE_CYCLE18.md §334.2). HMAC-SHA256 of the owner's NORMALIZED
/// (<c>PhoneNormalizer</c>) phone number, on its OWN key <c>Trial:PhoneKeyHmac</c> — never
/// <c>PhoneVerification:ExternalKeyHmac</c> and never <c>Notifications:EncryptionKey</c> (К2): distinct
/// processing purposes must not share a key, or two databases built for incompatible purposes become
/// linkable (ч. 5 ст. 5 152-ФЗ). Same technique as
/// <see cref="ServiceBooking.API.Services.PhoneVerification.ExternalAccountKey"/> — equality search is
/// needed, not decryption.
/// 🔴 Key rotation = registry destruction (К3): a value computed on a retired key is never compared
/// against anything again. Every stored value carries the <c>KeyId</c> it was computed with.
/// 🔴 Email is deliberately NOT part of the computed value (Р1).
/// </summary>
public static class TrialPhoneKey
{
    private const string Prefix = "trial-phone:";

    /// <param name="hmacKeyBase64">Base64-encoded key, at least 32 bytes — <c>Trial:PhoneKeyHmac</c>.</param>
    /// <param name="canonicalPhone">Output of <c>PhoneNormalizer.Normalize</c> — digits only.</param>
    /// <returns>Lowercase hex HMAC-SHA256, stored verbatim on <c>TrialPhoneRegistration.PhoneKeyHash</c>.
    /// Never round-trippable back to the phone number, and never present in any DTO.</returns>
    public static string Compute(string hmacKeyBase64, string canonicalPhone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hmacKeyBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPhone);

        var key = Convert.FromBase64String(hmacKeyBase64);
        var data = Encoding.UTF8.GetBytes(Prefix + canonicalPhone);
        var hmac = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hmac).ToLowerInvariant();
    }

    /// <summary>
    /// Fail-closed validity check (Д6/К1): true only if the key decodes from base64 and is at least 32
    /// bytes. Anything else — missing, malformed, too short — must never silently compute a weak or
    /// wrong-purpose key; callers treat a false result as "uniqueness check unavailable", not as "skip
    /// the check".
    /// </summary>
    public static bool IsKeyUsable(string? hmacKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(hmacKeyBase64)) return false;
        try
        {
            return Convert.FromBase64String(hmacKeyBase64).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// К2 — the mechanical part of "the key must be different from the other two subsystems' keys".
    /// Compared as decoded bytes, not as base64 text, so re-encoding the same key differently (unlikely
    /// but not forbidden) still gets caught.
    /// </summary>
    public static bool KeysCollide(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return Convert.FromBase64String(a).AsSpan().SequenceEqual(Convert.FromBase64String(b));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
