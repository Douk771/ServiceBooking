using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>ARCHITECTURE_CYCLE40.md §40.11.1 — the version stored with a messenger tick when the lawyer's text is not in the manifest yet:
/// <c>fallback:&lt;sha256 of the text key&gt;</c> (the same device as the registry attestation of «Дома»), so a later reader can tell the tick
/// was made before the text existed.</summary>
public static class MessengerConsentVersions
{
    public static string Fallback(string textKey) =>
        "fallback:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(textKey))).ToLowerInvariant();
}
