using ServiceBooking.API.Services.PhoneVerification;

namespace ServiceBooking.API.Services.StaffMax;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §497.2 — the opaque, unlinkable key of a MAX chat: the HMAC of the chat id under its OWN domain prefix ("max-chat:"), so it can
/// never coincide with the key of a MAX user (<see cref="ExternalAccountKey"/>). Equality search is possible (deduplication by chat); the id is not
/// recoverable from it. Pure.
/// </summary>
public static class StaffMaxChatKey
{
    public const string DomainPrefix = "max-chat:";

    public static string Compute(string hmacKeyBase64, string chatId) => ExternalAccountKey.Compute(hmacKeyBase64, DomainPrefix + chatId);
}
