using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// Computes the opaque, unlinkable identifier a MAX account is recognized by inside this subsystem
/// (ARCHITECTURE_CYCLE14.md §142.3, Q3). Deliberately HMAC, not AES-GCM
/// (<c>Notifications.SecretProtector</c>): the ceiling in §147.5 needs equality search over a MAX
/// account id, which a randomized-nonce cipher cannot support — two encryptions of the same plaintext
/// produce different ciphertexts by design. HMAC gives that equality search back while staying
/// irreversible (no code path in this codebase ever needs the raw MAX account id back).
///
/// Uses its OWN key (<c>PhoneVerification:ExternalKeyHmac</c>) — never
/// <c>Notifications:EncryptionKey</c> (§142.3's own explanation of why reusing that key would be wrong).
/// </summary>
public static class ExternalAccountKey
{
    private const string Prefix = "max:";

    /// <param name="hmacKeyBase64">Base64-encoded 32-byte key — <c>PhoneVerification:ExternalKeyHmac</c>.</param>
    /// <param name="maxUserId">The platform's own numeric/string user id for the MAX account that just
    /// interacted with the bot.</param>
    /// <returns>Lowercase hex HMAC-SHA256, stored verbatim on <c>VerifiedPhone.ExternalAccountKey</c> and
    /// <c>PhoneVerificationSession.ExternalAccountKey</c>. Never round-trippable back to
    /// <paramref name="maxUserId"/>, and never present in any DTO.</returns>
    public static string Compute(string hmacKeyBase64, string maxUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hmacKeyBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(maxUserId);

        var key = Convert.FromBase64String(hmacKeyBase64);
        var data = Encoding.UTF8.GetBytes(Prefix + maxUserId);
        var hmac = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hmac).ToLowerInvariant();
    }
}
