using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Thin wrapper over <see cref="SecretProtector"/> (cycle 4, §24) for one field — the health/
/// contraindication note (ARCHITECTURE_CYCLE5.md §48.1). Explicit calls, not an EF value converter: a
/// converter would decrypt transparently on ANY read, including a future projection nobody wrote with
/// this field in mind — exactly the "заодно"-leak §16 п. 6 requires excluded. Not calling this method
/// means not decrypting; there is no other path to the plaintext.
///
/// AAD binds the ciphertext to "company + subject" — a row copied into a different company's data (or a
/// different subject within the same company) does not decrypt, the same defence
/// <see cref="SecretProtector"/> already gives channel secrets via the channel id.
/// </summary>
public sealed class HealthNoteProtector(IOptions<NotificationOptions> notificationOptions)
{
    // Deliberately reuses Notifications:EncryptionKey (ARCHITECTURE_CYCLE5.md §42 table: "Механизм есть,
    // проверен... вторая криптография — вторая процедура ротации") rather than a second configured key —
    // one key, one rotation procedure, one way to lose data.
    private string Key => notificationOptions.Value.EncryptionKey
        ?? throw new InvalidOperationException("Notifications:EncryptionKey is not configured.");

    public string Protect(string plaintext, Guid companyId, string subjectKey) =>
        SecretProtector.Encrypt(plaintext, Key, AssociatedData(companyId, subjectKey));

    /// <summary>Same value <see cref="Protect"/> embeds in its own ciphertext prefix — exposed
    /// separately so the caller can store it on <c>ClientHealthNote.KeyId</c> too (mirrors
    /// <c>NotificationChannel.ProviderSecretKeyId</c>'s redundant-but-queryable-without-decrypting
    /// pattern, §24.4/§24.5).</summary>
    public string CurrentKeyId => SecretProtector.ComputeKeyId(SecretProtector.DecodeKey(Key));

    /// <summary>Null on ANY failure (wrong/rotated key, corrupted ciphertext, AAD mismatch) — never
    /// throws. §48.1: the caller turns this into "данные недоступны, обратитесь к платформе", not a 500
    /// on an arbitrary read.</summary>
    public string? Unprotect(string ciphertext, Guid companyId, string subjectKey)
    {
        try
        {
            return SecretProtector.Decrypt(ciphertext, Key, AssociatedData(companyId, subjectKey));
        }
        catch (ChannelSecretUnavailableException)
        {
            return null;
        }
    }

    private static string AssociatedData(Guid companyId, string subjectKey) => $"health:{companyId}:{subjectKey}";
}
