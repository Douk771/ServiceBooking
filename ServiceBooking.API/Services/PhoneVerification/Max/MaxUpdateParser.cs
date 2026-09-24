using System.Text.Json;

namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>A <c>contact</c> attachment inside a <c>message_created</c> update, as far as this subsystem
/// cares (ARCHITECTURE_CYCLE14.md §147). <see cref="OwnerId"/> comes from <c>max_info</c>, falling back
/// to <c>tam_info</c> when the former is absent — the contract (§167, <c>MaxUpdate</c> schema) carries
/// both because which one the live platform actually populates was unconfirmed at write time.</summary>
public sealed record MaxContactAttachment(string? VcfInfo, string? Hash, string? OwnerId);

/// <summary>Everything <c>MaxWebhookController</c> needs out of one MAX update, already reduced to plain
/// values (ARCHITECTURE_CYCLE14.md §144.3 — this class has no DB/HTTP dependency, only
/// <see cref="System.Text.Json"/>). Unrecognized fields are ignored everywhere, matching §167's "апдейт —
/// минимальная форма, неизвестные поля игнорируются".</summary>
public sealed record MaxUpdateData(string? UpdateType, string? Payload, string? SenderId, string? ChatId, MaxContactAttachment? Contact);

public static class MaxUpdateParser
{
    /// <summary>
    /// Reads <paramref name="root"/> (the webhook's JSON body) into a <see cref="MaxUpdateData"/>. Never
    /// throws on a missing/malformed field — an update this subsystem doesn't recognize the shape of
    /// simply produces nulls, which the caller treats as "nothing to do here", never as a parse error
    /// (§146.2 — a webhook exception must never turn into a non-2xx response).
    /// </summary>
    public static MaxUpdateData Parse(JsonElement root)
    {
        var updateType = GetString(root, "update_type");
        var payload = GetString(root, "payload");
        string? senderId = null;
        string? chatId = null;
        MaxContactAttachment? contact = null;

        if (TryGetObject(root, "message", out var message))
        {
            if (TryGetObject(message, "sender", out var sender))
                senderId = GetIdString(sender, "user_id");

            if (TryGetObject(message, "recipient", out var recipient))
                chatId = GetIdString(recipient, "chat_id");

            if (TryGetObject(message, "body", out var body) &&
                body.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
            {
                foreach (var attachment in attachments.EnumerateArray())
                {
                    var type = GetString(attachment, "type");
                    if (!string.Equals(type, "contact", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!TryGetObject(attachment, "payload", out var attachmentPayload)) continue;

                    var vcfInfo = GetString(attachmentPayload, "vcf_info");
                    var hash = GetString(attachmentPayload, "hash");
                    var ownerId = TryGetObject(attachmentPayload, "max_info", out var maxInfo) ? GetIdString(maxInfo, "user_id") : null;
                    ownerId ??= TryGetObject(attachmentPayload, "tam_info", out var tamInfo) ? GetIdString(tamInfo, "user_id") : null;

                    // §147.3 does not describe multiple contact attachments in one message — the first
                    // one is what's evaluated; there is no vocabulary in this subsystem for "which of
                    // several contacts did the person mean".
                    contact = new MaxContactAttachment(vcfInfo, hash, ownerId);
                    break;
                }
            }
        }

        // bot_started carries the sender at the top level (`user`), not inside `message`.
        if (senderId is null && TryGetObject(root, "user", out var user))
            senderId = GetIdString(user, "user_id");
        if (chatId is null && TryGetObject(root, "chat", out var chat))
            chatId = GetIdString(chat, "chat_id");

        return new MaxUpdateData(updateType, payload, senderId, chatId, contact);
    }

    private static bool TryGetObject(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
            return true;
        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>MAX ids may arrive as either a JSON number or a string, depending on the field — read
    /// either shape into the same canonical string form this subsystem hashes/compares.</summary>
    private static string? GetIdString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }
}
