using System.Text.Json;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.GreenApiMax;

/// <summary>
/// Parses a GREEN-API MAX webhook POST body — the MAX counterpart of
/// <see cref="GreenApi.GreenApiWebhookParser"/>, registered separately (keyed by
/// <see cref="NotificationTransport.Max"/>) so <c>NotificationsController</c>'s new
/// <c>provider-webhook/{transport}/{token}</c> route (ARCHITECTURE_CYCLE9.md §104.7) never sees a
/// provider-specific field name or string value either. B1 (§104.9) found MAX's webhook JSON shape
/// (<c>typeWebhook</c>, <c>chatId</c>, <c>instanceData</c>, <c>timestamp</c>, <c>idMessage</c>,
/// <c>status</c>) IDENTICAL to WhatsApp's, and — notably — MAX's <c>outgoingMessageStatus</c> status
/// vocabulary reuses the exact SAME string <c>"noAccount"</c> WhatsApp uses, plus a <c>"failed"</c> value
/// (a GENERIC delivery failure, not specifically "no account") the provider's own docs mark as
/// mandatory-to-handle. Unlike <see cref="GreenApi.GreenApiWebhookParser"/> (which folds both into one
/// <see cref="ProviderMessageStatus.Failed"/> and lets the CONTROLLER assume a single, WhatsApp-flavoured
/// terminal reason), this parser tells the controller WHICH terminal reason applies via
/// <see cref="ProviderCallback.TerminalReason"/> — <see cref="NotificationReason.RecipientNotInMax"/> for
/// <c>noAccount</c>, <see cref="NotificationReason.RejectedByProvider"/> for a generic <c>failed</c> —
/// rather than reusing WhatsApp's <see cref="NotificationReason.RecipientHasNoWhatsApp"/>, which would
/// simply be the wrong sentence in the delivery log for a MAX channel.
/// </summary>
public sealed class GreenApiMaxWebhookParser : IProviderWebhookParser
{
    public ProviderCallback? Parse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var typeWebhook = GetString(root, "typeWebhook");
            var instanceId = GetInstanceId(root);
            var occurredAtUtc = GetTimestamp(root);

            return typeWebhook switch
            {
                "outgoingMessageStatus" => ParseDeliveryStatus(root, instanceId, occurredAtUtc),
                "stateInstanceChanged" => ParseChannelState(root, instanceId, occurredAtUtc),
                _ => null,
            };
        }
    }

    private static ProviderCallback? ParseDeliveryStatus(JsonElement root, string? instanceId, DateTime occurredAtUtc)
    {
        var idMessage = GetString(root, "idMessage");
        var (status, terminalReason) = ParseMessageStatus(GetString(root, "status"));
        if (status is null) return null;

        return new ProviderCallback(
            ProviderCallbackKind.DeliveryStatus, idMessage, instanceId, status, ChannelState: null, occurredAtUtc, terminalReason);
    }

    private static ProviderCallback ParseChannelState(JsonElement root, string? instanceId, DateTime occurredAtUtc)
    {
        var state = GreenApiMaxStateInstanceParser.Parse(GetString(root, "stateInstance"));
        return new ProviderCallback(
            ProviderCallbackKind.ChannelState, ProviderMessageId: null, instanceId, MessageStatus: null, state, occurredAtUtc);
    }

    // B1 confirmed against MAX's OutgoingMessageStatus doc: delivered/read/failed/noAccount/notInGroup.
    // notInGroup (sender not a member of the target group chat) can never apply to this platform's own
    // 1:1 client messages and is deliberately left unmapped, same treatment as an unrecognised value.
    private static (ProviderMessageStatus? Status, NotificationReason? TerminalReason) ParseMessageStatus(string? raw) => raw switch
    {
        "sent" => (ProviderMessageStatus.Sent, null),
        "delivered" => (ProviderMessageStatus.Delivered, null),
        "read" => (ProviderMessageStatus.Read, null),
        "noAccount" => (ProviderMessageStatus.Failed, NotificationReason.RecipientNotInMax),
        "failed" => (ProviderMessageStatus.Failed, NotificationReason.RejectedByProvider),
        _ => (null, null),
    };

    private static string? GetInstanceId(JsonElement root)
    {
        if (!root.TryGetProperty("instanceData", out var instanceData) || instanceData.ValueKind != JsonValueKind.Object)
            return null;
        if (!instanceData.TryGetProperty("idInstance", out var idInstance)) return null;

        return idInstance.ValueKind switch
        {
            JsonValueKind.String => idInstance.GetString(),
            JsonValueKind.Number => idInstance.GetRawText(),
            _ => null,
        };
    }

    private static DateTime GetTimestamp(JsonElement root) =>
        root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : DateTime.UtcNow;

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
